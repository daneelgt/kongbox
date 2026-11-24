using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using KongBox.Agent;
using Microsoft.Win32;

// Hook mode: Claude Code runs "KongBoxAgent.exe --claude-hook" for every permission request.
if (args.Contains("--claude-hook")) return await ClaudeHook.RunHookProcess();
if (args.Contains("--uninstall")) return Installer.Uninstall();
#if !DEBUG
// the release exe is its own installer: offer to install (or update) unless it already runs from the install folder
if (!args.Contains("--portable") && !args.Contains("--restart-wait") && Installer.OfferInstall()) return 0;
#endif

const int Port = 8765;
const string Url = "http://127.0.0.1:8765/";

// "--restart-wait <pid>": started by /api/restart; wait for the old agent to let go of the single-instance lock
var waitIdx = Array.IndexOf(args, "--restart-wait");
if (waitIdx >= 0 && waitIdx + 1 < args.Length && int.TryParse(args[waitIdx + 1], out var oldPid))
{
    try { using var old = Process.GetProcessById(oldPid); old.WaitForExit(15_000); } catch { /* already gone */ }
}
using var single = new Mutex(true, "Global\\KongBoxAgent", out var first);
if (!first)
{
    // already running: bring its window forward instead of starting a second agent
    try
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var req = new HttpRequestMessage(HttpMethod.Post, Url + "api/window/show");
        req.Headers.Add("X-Kong", "1");
        hc.Send(req);
    }
    catch { Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true })?.Dispose(); }
    return 0;
}

var config = new ConfigStore();
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("KongBox/0.1 (+desk panel)");
var sys = new SystemMonitor(config);
var procs = new ProcessMonitor();
var media = new MediaMonitor();
var push = new PushStore();
var shortcuts = new Shortcuts(config, () => media);
var rules = new RuleEngine(config, sys, procs, push, shortcuts);
var sessions = new SessionMonitor();
var quotas = new QuotaMonitor(config, sessions, http);
var github = new GitHubMonitor(config, http, new LocalGit(config, () => sessions.Roots()));
var approvals = new ApprovalStore();
var adb = new AdbReverse(config);
var feeds = new FeedProxy(http);
var lan = new Lan();
var apkShare = new ApkShare();
var bluetooth = new BluetoothBridge(config);
var mirror = new NotificationMirror(config, push);
var lanEnabled = config.Snapshot()["lan"].Bool("enabled");
long reloadSeq = 0;
var cts = new CancellationTokenSource();

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(k =>
{
    k.Listen(System.Net.IPAddress.Loopback, Port);
    // Wi-Fi: HTTPS only, the phone pins this certificate's fingerprint
    if (lanEnabled) k.ListenAnyIP(Lan.Port, l => l.UseHttps(lan.Certificate));
});
builder.Logging.ClearProviders();
var app = builder.Build();

// ---- loopback guard: DNS rebinding (Host) and cross-site requests (Origin / X-Kong) ----
static bool IsWebhook(string path) =>
    path.StartsWith("/api/push") || path == "/api/notify" || path.StartsWith("/api/goto/") || path == "/api/refresh";

// ---- Wi-Fi port: only the panel's API, only for paired phones (cookie "kb" or Bearer token) ----
app.Use(async (ctx, next) =>
{
    if (ctx.Connection.LocalPort != Lan.Port) { await next(); return; }
    var path = ctx.Request.Path.Value ?? "";
    var method = ctx.Request.Method;
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
    if (path == "/panel") { ctx.Response.Redirect("/panel/"); return; }
    if (path == "/api/hello" || (path == "/api/pair/claim" && HttpMethods.IsPost(method))) { await next(); return; }
    // same-origin only for anything that changes state
    var origin = ctx.Request.Headers.Origin.ToString();
    if (!HttpMethods.IsGet(method) && origin != "" && origin != $"https://{ctx.Request.Host.Value}")
    {
        ctx.Response.StatusCode = 403;
        return;
    }
    var auth = ctx.Request.Headers.Authorization.ToString();
    var bearer = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..].Trim() : null;
    // webhooks from other machines use the webhook key, never a phone token
    if (IsWebhook(path))
    {
        var key = config.Snapshot().Str("webhookKey");
        var given = bearer ?? ctx.Request.Query["token"].ToString();
        if (key.Length >= 16 && given == key) { await next(); return; }
        ctx.Response.StatusCode = 401;
        return;
    }
    if (!Lan.PanelPath(path, method)) { ctx.Response.StatusCode = 404; return; }
    if (path.StartsWith("/panel/")) { await next(); return; } // static files hold no secrets
    if (!lan.Validate(ctx.Request.Cookies["kb"] ?? bearer)) { ctx.Response.StatusCode = 401; return; }
    if (!HttpMethods.IsGet(method) && ctx.Request.Headers["X-Kong"] != "1") { ctx.Response.StatusCode = 403; return; }
    await next();
});

app.Use(async (ctx, next) =>
{
    if (ctx.Connection.LocalPort == Lan.Port) { await next(); return; }
    var host = ctx.Request.Host.Value ?? "";
    if (host is not ("127.0.0.1:8765" or "localhost:8765"))
    {
        ctx.Response.StatusCode = 403;
        return;
    }
    var origin = ctx.Request.Headers.Origin.ToString();
    var sameOrigin = origin == "" || origin is "http://127.0.0.1:8765" or "http://localhost:8765";
    var path = ctx.Request.Path.Value ?? "";
    // routing ignores a trailing slash, so "/panel" → "/panel/" is done here (relative asset URLs need the slash)
    if (path == "/panel") { ctx.Response.Redirect("/panel/"); return; }
    if (path.StartsWith("/api/") && !HttpMethods.IsGet(ctx.Request.Method))
    {
        var kong = ctx.Request.Headers["X-Kong"] == "1";
        if (!sameOrigin || (!kong && !IsWebhook(path)))
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new { error = "Requisição bloqueada (origem ou header X-Kong ausente)." });
            return;
        }
    }
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

static IResult JsonResult(JsonNode? node, int status = 200) =>
    Results.Text(node?.ToJsonString(Json.Web) ?? "null", "application/json; charset=utf-8", Encoding.UTF8, status);

static IResult Ok(string msg = "ok") => JsonResult(new JsonObject { ["ok"] = true, ["msg"] = msg });
static IResult Fail(string msg, int status = 400) => JsonResult(new JsonObject { ["ok"] = false, ["error"] = msg }, status);

static async Task<string> Body(HttpRequest r)
{
    using var reader = new StreamReader(r.Body, Encoding.UTF8);
    var text = await reader.ReadToEndAsync();
    return text.Length > 256_000 ? text[..256_000] : text;
}

static async Task<JsonNode?> BodyJson(HttpRequest r)
{
    try { return Json.Parse(await Body(r)); } catch { return null; }
}

// ---- static pages ----
static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
{
    ".html" => "text/html; charset=utf-8",
    ".js" => "text/javascript; charset=utf-8",
    ".css" => "text/css; charset=utf-8",
    ".svg" => "image/svg+xml",
    ".png" => "image/png",
    ".json" => "application/json; charset=utf-8",
    ".ico" => "image/x-icon",
    _ => "application/octet-stream",
};

// Debug builds serve the panel and the editor straight from the source tree, so a refresh shows an edit.
string? devRoot = null;
#if DEBUG
for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
    if (Directory.Exists(Path.Combine(d.FullName, "android", "app", "src", "main", "assets", "www"))) { devRoot = d.FullName; break; }
if (devRoot is not null) Log.Info("dev mode: serving UI from " + devRoot);
#endif

app.MapGet("/", (HttpContext ctx) =>
{
    ctx.Response.Headers.CacheControl = "no-cache";
    var dev = devRoot is null ? null : Path.Combine(devRoot, "agent", "KongBox.Agent", "settings.html");
    return Results.Text(dev is not null && File.Exists(dev) ? File.ReadAllText(dev) : Resources.Text("settings.html"), "text/html; charset=utf-8");
});
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/panel/{*file}", (string? file, HttpContext ctx) =>
{
    file = string.IsNullOrEmpty(file) ? "index.html" : file.Replace('\\', '/');
    if (file.Contains("..")) return Results.NotFound();
    var devFile = devRoot is null ? null : Path.Combine(devRoot, "android", "app", "src", "main", "assets", "www", file);
    var bytes = devFile is not null && File.Exists(devFile) ? File.ReadAllBytes(devFile) : Resources.Bytes("www/" + file);
    if (bytes is null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "no-cache";
    return Results.Bytes(bytes, ContentType(file));
});

// ---- panel data ----
app.MapGet("/api/config", () => JsonResult(config.PanelConfig()));
app.MapGet("/api/catalog", () => JsonResult(new JsonObject
{
    ["widgets"] = ConfigStore.Catalog.DeepClone(),
    ["templates"] = ConfigStore.Templates.DeepClone(),
}));

app.MapGet("/api/state", (HttpRequest r) =>
{
    var full = r.Query["full"] == "1";
    long.TryParse(r.Query["since"], out var since);
    var o = new JsonObject
    {
        ["t"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        ["config"] = config.Version,
        ["reload"] = Interlocked.Read(ref reloadSeq),
        ["sys"] = sys.Snapshot(),
        ["media"] = media.State(),
        ["volume"] = AudioControl.State(),
        ["approvals"] = approvals.ToJson(),
        ["notices"] = push.Notices(since, 10),
        ["lastNotice"] = push.LastNoticeId,
        ["goto"] = push.PendingGoto(),
        ["muted"] = AudioControl.IsMuted(),
    };
    if (full)
    {
        o["hist"] = sys.HistoryJson();
        o["procs"] = procs.ToJson(40);
        o["quotas"] = quotas.ToJson();
        o["sessions"] = sessions.ToJson();
        o["github"] = github.ToJson();
        o["push"] = push.Values();
        o["rules"] = rules.Status();
        o["rulesPausedUntil"] = rules.PausedUntil > DateTime.UtcNow ? new DateTimeOffset(rules.PausedUntil).ToUnixTimeMilliseconds() : null;
        o["inbox"] = push.Notices(0, 30);
    }
    return JsonResult(o);
});

app.MapGet("/api/art", (HttpRequest r) =>
{
    var (data, type) = media.Art();
    if (data is null) return Results.NotFound();
    // ?small=1: a 32 px copy the panel stretches as its blurred backdrop (cheap on old GPUs: no CSS blur filter)
    if (r.Query["small"] == "1")
    {
        try
        {
            using var src = new System.Drawing.Bitmap(new MemoryStream(data));
            using var dst = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, 32, 32);
            }
            using var ms = new MemoryStream();
            dst.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Results.Bytes(ms.ToArray(), "image/png");
        }
        catch { /* unreadable artwork: fall back to the original */ }
    }
    return Results.Bytes(data, type);
});

app.MapGet("/api/proxy", async (HttpRequest r, CancellationToken ct) =>
{
    if (!FeedProxy.Allowed(r.Query["u"].ToString(), out var uri)) return Fail("Host não permitido.", 403);
    var (status, type, body) = await feeds.Get(uri, ct);
    return status == 200 ? Results.Bytes(body, type) : Fail($"Fonte respondeu {status}.", 502);
});

app.MapGet("/api/info", () => JsonResult(new JsonObject
{
    ["name"] = "KONGBOX",
    ["version"] = typeof(ConfigStore).Assembly.GetName().Version?.ToString(3),
    ["host"] = Environment.MachineName,
    ["os"] = Environment.OSVersion.VersionString,
    ["configPath"] = ConfigStore.FilePath,
    ["adb"] = adb.AdbPath,
    ["adbError"] = adb.Error,
    ["apk"] = ApkShare.ApkPath(),
    ["installed"] = Installer.RunningInstalled,
    ["hookInstalled"] = ClaudeHook.Installed(),
    ["autostart"] = Autostart.Enabled(),
}));

// ---- actions from the phone (ids only) ----
app.MapPost("/api/run/{id}", async (string id) =>
{
    var (ok, msg) = await shortcuts.Run(id);
    return ok ? Ok(msg) : Fail(msg);
});

app.MapPost("/api/media/{action}", async (string action, HttpRequest r) =>
{
    double? sec = double.TryParse(r.Query["sec"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null;
    return await media.Action(action, sec) ? Ok() : Fail("Sem player ativo.");
});

app.MapPost("/api/volume", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    int? level = b?["level"] is null ? null : (int)b.Num("level");
    bool? muted = b?["muted"] is null ? null : b.Bool("muted");
    if (b.Bool("toggleMute")) return AudioControl.ToggleMute() ? JsonResult(AudioControl.State()) : Fail("Sem saída de áudio.");
    return AudioControl.Set(level, muted) ? JsonResult(AudioControl.State()) : Fail("Sem saída de áudio.");
});

app.MapGet("/api/procs", (HttpRequest r) => JsonResult(procs.ToJson(60, r.Query["sort"] == "mem" ? "mem" : "cpu")));
app.MapPost("/api/procs/kill", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    var (ok, msg) = procs.Kill(b.Str("name"), b.Bool("all"));
    return ok ? Ok(msg) : Fail(msg);
});

app.MapPost("/api/approvals/{id}", async (string id, HttpRequest r) =>
{
    var b = await BodyJson(r);
    return approvals.Answer(id, b.Bool("allow")) ? Ok() : Fail("Pedido expirado.", 404);
});

app.MapPost("/api/widget/{pageId}/{widgetId}/note", async (string pageId, string widgetId, HttpRequest r) =>
{
    var b = await BodyJson(r);
    var text = b.Str("text");
    if (text.Length > 20_000) return Fail("Nota grande demais.");
    var cfg = config.Snapshot();
    var w = cfg.Arr("pages").FirstOrDefault(p => p.Str("id") == pageId)?.Arr("widgets").FirstOrDefault(x => x.Str("id") == widgetId) as JsonObject;
    if (w is null || w.Str("type") != "note") return Fail("Nota não encontrada.", 404);
    var opts = w["options"] as JsonObject ?? new JsonObject();
    opts["text"] = text;
    w["options"] = opts;
    config.Save(cfg);
    return Ok();
});

app.MapPost("/api/rules/pause", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    rules.Pause((int)b.Num("minutes", 60));
    return Ok();
});
app.MapPost("/api/rules/test", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    var rule = config.Snapshot().Arr("rules").FirstOrDefault(x => x.Str("id") == b.Str("id"));
    if (rule is null) return Fail("Regra não encontrada.", 404);
    await rules.Fire(rule);
    return Ok("Ações disparadas.");
});

// ---- webhooks ----
app.MapPost("/api/push/{key}", async (string key, HttpRequest r) =>
{
    if (key.Length is 0 or > 64) return Fail("Chave inválida.");
    int? ttl = int.TryParse(r.Query["ttl"], out var t) ? t : null;
    return JsonResult(push.Set(key, await Body(r), ttl));
});
app.MapGet("/api/push", () => JsonResult(push.Values()));
app.MapGet("/api/push/{key}", (string key) => JsonResult(push.Values()[key]?.DeepClone()));
app.MapDelete("/api/push/{key}", (string key) => push.Remove(key) ? Ok() : Fail("Não existe.", 404));
app.MapDelete("/api/push", () => { push.Clear(); return Ok(); });
app.MapPost("/api/notify", async (HttpRequest r) =>
{
    var b = await BodyJson(r) as JsonObject;
    if (b is null) return Fail("Envie JSON { title, text, level }.");
    return JsonResult(push.Notify(b));
});
app.MapDelete("/api/notify", () => { push.ClearNotices(); return Ok("Notificações apagadas."); });
app.MapDelete("/api/notify/{id:long}", (long id) => push.RemoveNotice(id) ? Ok() : Fail("Não existe.", 404));
// opens the Windows app that sent a mirrored notification (the phone sends the notice id only)
app.MapPost("/api/notify/{id:long}/open", (long id) =>
{
    var n = push.Notice(id);
    if (n is null) return Fail("Notificação não encontrada.", 404);
    return NotificationMirror.OpenApp(n.Str("aumid")) ? Ok("Abrindo " + n.Str("app", "o app") + " no PC.") : Fail("Essa notificação não veio de um app do Windows.");
});
// a web page on the PC's browser: https only (Hacker News stories, exchange and crypto sources)
app.MapPost("/api/open", async (HttpRequest r) =>
{
    var url = (await BodyJson(r)).Str("url");
    if (url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https") return Fail("Só endereços https.");
    Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    return Ok("Aberto no PC.");
});
app.MapPost("/api/github/open", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    var url = github.Url(b.Str("repo"), b?["pr"] is null ? null : (int)b.Num("pr"), b?["run"] is null ? null : (long)b.Num("run"));
    if (url is null) return Fail("Repositório fora da sua lista.");
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    return Ok("Aberto no navegador do PC.");
});
app.MapPost("/api/github/act", async (HttpRequest r, CancellationToken ct) =>
{
    var b = await BodyJson(r);
    var (ok, msg) = await github.Act(b.Str("action"), b.Str("repo"), b?["pr"] is null ? null : (int)b.Num("pr"), b?["run"] is null ? null : (long)b.Num("run"), ct);
    return ok ? Ok(msg) : Fail(msg);
});
app.MapPost("/api/github/local", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    var (ok, msg) = await Task.Run(() => github.LocalAct(b.Str("repo"), b.Str("action")));
    return ok ? Ok(msg) : Fail(msg);
});
// repositories cloned on this PC (the editor offers them as one-tap picks)
app.MapGet("/api/github/clones", () => JsonResult(new JsonArray(github.Local.Clones(force: true).Select(kv => (JsonNode)new JsonObject { ["repo"] = kv.Key, ["path"] = kv.Value }).ToArray())));
app.MapPost("/api/sessions/open", async (HttpRequest r) =>
{
    var (ok, msg) = sessions.OpenInEditor((await BodyJson(r)).Str("id"));
    return ok ? Ok(msg) : Fail(msg);
});
app.MapPost("/api/goto/{page}", (string page, HttpRequest r) =>
{
    push.Goto(page, int.TryParse(r.Query["countdown"], out var c) ? c : 3);
    return Ok();
});
// the panel reports its engine and script errors (diag.js): one log line each, capped so a loop cannot flood the log
var clientLogs = 0;
var clientLogWindow = DateTime.UtcNow;
app.MapPost("/api/clientlog", async (HttpRequest r) =>
{
    if (DateTime.UtcNow - clientLogWindow > TimeSpan.FromMinutes(1)) { clientLogWindow = DateTime.UtcNow; clientLogs = 0; }
    if (++clientLogs > 30) return Ok();
    var b = await BodyJson(r) as JsonObject;
    if (b is null) return Ok();
    string Cut(string s) => s.Length > 300 ? s[..300] : s;
    var via = r.HttpContext.Connection.LocalPort == Lan.Port ? "wifi" : "usb/bluetooth/pc";
    if (b.Str("kind") == "error")
        Log.Warn($"panel error ({via}): {Cut(b.Str("msg"))} @ {Cut(b.Str("src"))}:{b.Num("line")} | {Cut(b.Str("ua"))}");
    else
        Log.Info($"panel ({via}) {Cut(b.Str("screen"))} cq={b["cq"]} ro={b["resizeObserver"]} pe={b["pointer"]} | {Cut(b.Str("ua"))}");
    return Ok();
});
app.MapPost("/api/refresh", (HttpRequest r) =>
{
    quotas.RefreshNow();
    github.RefreshNow();
    if (r.Query["reload"] == "1") Interlocked.Increment(ref reloadSeq);
    return Ok();
});

// ---- Claude Code hook ----
app.MapPost("/api/claude/hook", async (HttpRequest r) =>
{
    var cfg = config.Snapshot()["claudeHook"];
    if (!cfg.Bool("enabled")) return JsonResult(new JsonObject { ["decision"] = null });
    var hook = await BodyJson(r);
    var decision = await approvals.Ask(hook, TimeSpan.FromSeconds(Math.Clamp(cfg.Num("timeoutSec", 50), 10, 55)));
    return JsonResult(new JsonObject { ["decision"] = decision is null ? null : decision.Value ? "allow" : "deny" });
});
app.MapPost("/api/claude/hook/install", () =>
{
    try { ClaudeHook.Install(); return Ok("Hook instalado em ~/.claude/settings.json (backup ao lado)."); }
    catch (Exception e) { return Fail(e.Message, 500); }
});
app.MapDelete("/api/claude/hook", () =>
{
    try { ClaudeHook.Uninstall(); return Ok("Hook removido."); }
    catch (Exception e) { return Fail(e.Message, 500); }
});

// ---- integrations: one place that says what is connected and what is missing ----
static bool OnPath(string exe) =>
    (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Any(d => new[] { ".exe", ".cmd", ".bat", "" }.Any(ext => File.Exists(Path.Combine(d.Trim('"'), exe + ext))));
app.MapGet("/api/integrations", () =>
{
    var cfg = config.Snapshot();
    var home = AppPaths.Home;
    var q = quotas.ToJson();
    JsonNode? Q(string p) => q.FirstOrDefault(x => x.Str("provider") == p);
    var claudeCred = Path.Combine(home, ".claude", ".credentials.json");
    var sess = sessions.ToJson(50);
    var gh = github.ToJson();
    var cq = Q("claude");
    var xq = Q("codex");
    return JsonResult(new JsonObject
    {
        ["claude"] = new JsonObject
        {
            ["enabled"] = cfg["quotas"].Bool("claude", true),
            ["cli"] = OnPath("claude"),
            ["loggedIn"] = File.Exists(claudeCred),
            ["sessions"] = sess.Count(s => s.Str("tool") == "claude"),
            ["plan"] = cq.Str("plan"),
            ["windows"] = cq?["windows"]?.DeepClone(),
            ["error"] = cq.Str("error"),
        },
        ["codex"] = new JsonObject
        {
            ["enabled"] = cfg["quotas"].Bool("codex", true),
            ["cli"] = OnPath("codex"),
            ["folder"] = Directory.Exists(Path.Combine(home, ".codex")),
            ["sessions"] = sess.Count(s => s.Str("tool") == "codex"),
            ["windows"] = xq?["windows"]?.DeepClone(),
            ["error"] = xq.Str("error"),
        },
        ["hook"] = new JsonObject { ["enabled"] = cfg["claudeHook"].Bool("enabled"), ["installed"] = ClaudeHook.Installed() },
        ["bluetooth"] = bluetooth.Status(),
        ["notifications"] = new JsonObject { ["enabled"] = cfg["notifications"].Bool("windows", true), ["available"] = mirror.Available, ["error"] = mirror.Error },
        ["github"] = new JsonObject
        {
            ["token"] = cfg["github"].Str("token") != "",
            ["repos"] = cfg["github"].Arr("repos").Count,
            ["me"] = gh.Str("me"),
            ["error"] = gh.Str("error"),
            ["updated"] = gh["updated"]?.DeepClone(),
        },
    });
});
// lists the repositories the GitHub token can see, most recently pushed first (for the picker in the editor)
app.MapPost("/api/github/repos", async (HttpRequest r, CancellationToken ct) =>
{
    var b = await BodyJson(r);
    var token = b.Str("token");
    if (token == "") token = config.Snapshot()["github"].Str("token");
    if (token == "") return Fail("Cole um token do GitHub primeiro.");
    using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/repos?sort=pushed&per_page=60&affiliation=owner,collaborator,organization_member");
    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    using var res = await http.SendAsync(req, ct);
    if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized) return Fail("Token inválido ou expirado.", 401);
    if (!res.IsSuccessStatusCode) return Fail($"GitHub respondeu {(int)res.StatusCode}.", 502);
    var list = Json.Parse(await res.Content.ReadAsStringAsync(ct)) as JsonArray ?? new JsonArray();
    return JsonResult(new JsonArray(list.Select(x => (JsonNode)new JsonObject
    {
        ["name"] = x.Str("full_name"), ["private"] = x.Bool("private"), ["pushed"] = x.Str("pushed_at"), ["description"] = x.Str("description"),
    }).ToArray()));
});

// ---- custom wallpaper: resized to 2560 px max, EXIF rotation applied, stored as JPEG ----
static string WallpaperPath() => AppPaths.File("wallpaper.jpg");
app.MapGet("/api/wallpaper", (HttpContext ctx) =>
{
    if (!File.Exists(WallpaperPath())) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=31536000"; // the panel asks with ?v=<version>
    return Results.File(WallpaperPath(), "image/jpeg");
});
app.MapPost("/api/wallpaper", async (HttpRequest r) =>
{
    using var buf = new MemoryStream();
    await r.Body.CopyToAsync(buf);
    if (buf.Length == 0 || buf.Length > 40_000_000) return Fail("Envie uma imagem de até 40 MB.");
    try
    {
        buf.Position = 0;
        using var src = new System.Drawing.Bitmap(buf);
        // photos from phones carry their rotation in EXIF tag 0x0112
        if (src.PropertyIdList.Contains(0x0112))
        {
            var o = src.GetPropertyItem(0x0112)?.Value?[0] ?? 1;
            var flip = o switch
            {
                3 => System.Drawing.RotateFlipType.Rotate180FlipNone,
                6 => System.Drawing.RotateFlipType.Rotate90FlipNone,
                8 => System.Drawing.RotateFlipType.Rotate270FlipNone,
                _ => System.Drawing.RotateFlipType.RotateNoneFlipNone,
            };
            src.RotateFlip(flip);
        }
        var scale = Math.Min(1.0, 2560.0 / Math.Max(src.Width, src.Height));
        int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
        using var dst = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(dst))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        var jpeg = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var ep = new System.Drawing.Imaging.EncoderParameters(1);
        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
        dst.Save(WallpaperPath(), jpeg, ep);
        return JsonResult(new JsonObject { ["ok"] = true, ["v"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["width"] = w, ["height"] = h, ["bytes"] = new FileInfo(WallpaperPath()).Length });
    }
    catch (Exception e) { return Fail("Não consegui ler essa imagem: " + e.Message); }
});
app.MapDelete("/api/wallpaper", () => { try { File.Delete(WallpaperPath()); } catch { } return Ok(); });

// ---- Wi-Fi pairing ----
app.MapGet("/api/hello", () => JsonResult(new JsonObject { ["name"] = "KONGBOX", ["host"] = Environment.MachineName }));
app.MapGet("/api/lan", () => { var s = lan.Status(lanEnabled); s["bluetooth"] = bluetooth.Status(); return JsonResult(s); });
app.MapPost("/api/pair/start", () => lanEnabled ? JsonResult(lan.StartPairing()) : Fail("Ative o Wi-Fi e reinicie o agente primeiro."));
app.MapPost("/api/pair/cancel", () => { lan.CancelPairing(); return Ok(); });
app.MapPost("/api/pair/claim", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    var (token, error) = lan.Claim(b.Str("code"), b.Str("name"));
    return token is null ? Fail(error ?? "Código inválido.", 403) : JsonResult(new JsonObject { ["token"] = token, ["host"] = Environment.MachineName });
});
app.MapDelete("/api/lan/phones/{id}", (string id) => lan.Revoke(id) ? Ok("Celular removido.") : Fail("Não encontrado.", 404));
app.MapPost("/api/window/show", () => { Tray.ShowWindow(); return Ok(); });
app.MapPost("/api/quit", () => { _ = Task.Run(async () => { await Task.Delay(300); cts.Cancel(); }); return Ok("Saindo…"); });
// install the phone app over Wi-Fi: a 10-minute plain HTTP download of the APK, with a QR code for the phone's camera
app.MapPost("/api/apk/share", () =>
{
    var (ok, msg) = apkShare.Start();
    if (!ok) return Fail(msg);
    var ip = Lan.Addresses().FirstOrDefault() ?? "127.0.0.1";
    var url = $"http://{ip}:{ApkShare.Port}/kongbox.apk";
    using var gen = new QRCoder.QRCodeGenerator();
    using var data = gen.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
    return JsonResult(new JsonObject { ["url"] = url, ["qr"] = new QRCoder.SvgQRCode(data).GetGraphic(6, "#000000", "#ffffff", true), ["until"] = apkShare.Until.ToUnixTimeMilliseconds() });
});
app.MapDelete("/api/apk/share", () => { apkShare.Stop(); return Ok(); });
app.MapPost("/api/restart", () =>
{
    // a fresh process waits for this one to exit, then takes over (Kestrel rebinds with the new Wi-Fi setting)
    Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--restart-wait {Environment.ProcessId}") { UseShellExecute = false })?.Dispose();
    _ = Task.Run(async () => { await Task.Delay(400); cts.Cancel(); });
    return Ok("Reiniciando…");
});

// ---- settings (editor) ----
app.MapGet("/api/settings", () => JsonResult(config.Snapshot()));
app.MapPut("/api/settings", async (HttpRequest r) =>
{
    if (await BodyJson(r) is not JsonObject next) return Fail("JSON inválido.");
    try { config.Save(next); return JsonResult(new JsonObject { ["ok"] = true, ["version"] = config.Version }); }
    catch (ArgumentException e) { return Fail(e.Message); }
});
app.MapPost("/api/shortcuts/test", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    if (b is null) return Fail("JSON inválido.");
    var (ok, msg) = await shortcuts.Execute(b);
    return ok ? Ok(msg) : Fail(msg);
});
app.MapGet("/api/geocode", async (HttpRequest r, CancellationToken ct) =>
{
    var q = Uri.EscapeDataString(r.Query["q"].ToString());
    var uri = new Uri($"https://geocoding-api.open-meteo.com/v1/search?name={q}&count=6&language=pt&format=json");
    var (status, type, body) = await feeds.Get(uri, ct);
    return status == 200 ? Results.Bytes(body, type) : Fail($"Busca respondeu {status}.", 502);
});
app.MapGet("/api/phones", () => JsonResult(adb.Phones()));
app.MapPost("/api/phone/install", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    var (ok, msg) = await Task.Run(() => adb.Install(b.Str("serial")));
    return ok ? Ok(msg) : Fail(msg);
});
app.MapPost("/api/phone/open", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    var (ok, msg) = adb.Open(b.Str("serial"));
    return ok ? Ok(msg) : Fail(msg);
});
app.MapPost("/api/autostart", async (HttpRequest r) =>
{
    var b = await BodyJson(r);
    Autostart.Set(b.Bool("enabled"));
    return Ok(Autostart.Enabled() ? "Inicia com o Windows." : "Não inicia com o Windows.");
});

Tray.Start(Url, () => cts.Cancel(), showWindow: args.Contains("--open"));
Log.Info($"KONGBOX agent listening on {Url}" + (lanEnabled ? $" and https://*:{Lan.Port}" : ""));

try { await app.RunAsync(cts.Token); }
catch (IOException e) { Log.Warn("server: " + e.Message); }
finally
{
    bluetooth.Dispose(); mirror.Dispose(); sys.Dispose(); procs.Dispose(); media.Dispose(); rules.Dispose(); sessions.Dispose(); quotas.Dispose(); github.Dispose(); adb.Dispose();
}
return 0;

static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "KongBox";

    public static bool Enabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Name) is string;
    }

    public static void Set(bool on, string? path = null)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (on) k.SetValue(Name, $"\"{path ?? Environment.ProcessPath}\"");
        else k.DeleteValue(Name, false);
    }
}
