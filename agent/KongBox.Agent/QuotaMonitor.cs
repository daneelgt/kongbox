using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>
/// Claude (your own OAuth token from ~/.claude/.credentials.json → api.anthropic.com usage) and Codex (rate limits
/// written by Codex itself into its session logs). Burn-rate forecast from the last hour of samples.
/// </summary>
sealed class QuotaMonitor : IDisposable
{
    readonly ConfigStore config;
    readonly SessionMonitor sessions;
    readonly HttpClient http;
    readonly CancellationTokenSource cts = new();
    readonly object gate = new();
    JsonObject? claude;
    string? claudeError;
    DateTimeOffset claudeAt;
    TimeSpan backoff = TimeSpan.FromMinutes(3);
    DateTimeOffset nextClaude = DateTimeOffset.MinValue;
    readonly Dictionary<string, List<(DateTimeOffset t, double pct)>> samples = new();

    public QuotaMonitor(ConfigStore config, SessionMonitor sessions, HttpClient http)
    {
        (this.config, this.sessions, this.http) = (config, sessions, http);
        Task.Run(() => Loop(cts.Token));
    }

    public void RefreshNow() => nextClaude = DateTimeOffset.MinValue;

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (config.Snapshot()["quotas"].Bool("claude", true) && DateTimeOffset.UtcNow >= nextClaude) await FetchClaude(ct);
            }
            catch (Exception e) { claudeError = e.Message; }
            try { await Task.Delay(5000, ct); } catch { break; }
        }
    }

    static string CredentialsPath => Path.Combine(AppPaths.Home, ".claude", ".credentials.json");

    async Task FetchClaude(CancellationToken ct)
    {
        nextClaude = DateTimeOffset.UtcNow + backoff;
        if (!File.Exists(CredentialsPath)) { claudeError = "Claude Code não está logado neste PC."; return; }
        var cred = Json.Parse(await File.ReadAllTextAsync(CredentialsPath, ct))?["claudeAiOauth"];
        var token = cred.Str("accessToken");
        if (token == "") { claudeError = "Sem token OAuth do Claude Code."; return; }
        // The token lasts ~8 h and only the Claude Code CLI renews it. The agent never refreshes it itself: refresh
        // tokens rotate, and renewing here could sign the CLI out. Try anyway; a 401 below explains what to do.
        var expired = cred.Num("expiresAt") is > 0 and var exp && DateTimeOffset.FromUnixTimeMilliseconds((long)exp) < DateTimeOffset.UtcNow;
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        using var res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
        {
            backoff = TimeSpan.FromMinutes(Math.Min(30, backoff.TotalMinutes * 2));
            nextClaude = DateTimeOffset.UtcNow + backoff;
            claudeError = "Limite de consultas; tentando de novo mais tarde.";
            return;
        }
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            claudeError = expired ? "Login vencido: rode \"claude\" no terminal uma vez para renovar." : "O Claude recusou o login deste PC.";
            // re-read the file soon: the CLI rewrites it as soon as it renews
            nextClaude = DateTimeOffset.UtcNow.AddMinutes(1);
            return;
        }
        if (!res.IsSuccessStatusCode) { claudeError = $"HTTP {(int)res.StatusCode}"; return; }
        backoff = TimeSpan.FromMinutes(3);
        var body = Json.Parse(await res.Content.ReadAsStringAsync(ct)) as JsonObject;
        lock (gate)
        {
            claude = body;
            claudeAt = DateTimeOffset.UtcNow;
            claudeError = null;
            var plan = cred.Str("subscriptionType");
            if (plan != "" && claude is not null) claude["_plan"] = plan;
        }
    }

    static readonly (string key, string id, string label)[] ClaudeWindows =
    [
        ("five_hour", "session", "Sessão (5 h)"),
        ("seven_day", "weekly", "Semanal"),
        ("seven_day_opus", "opus", "Semanal Opus"),
        ("seven_day_sonnet", "sonnet", "Semanal Sonnet"),
    ];

    public JsonArray ToJson()
    {
        var cfg = config.Snapshot()["quotas"];
        var list = new JsonArray();
        if (cfg.Bool("claude", true)) list.Add(ClaudeJson());
        if (cfg.Bool("codex", true)) list.Add(CodexJson());
        return list;
    }

    JsonObject ClaudeJson()
    {
        var windows = new JsonArray();
        JsonObject? c;
        lock (gate) c = claude;
        if (c is not null)
            foreach (var (key, id, label) in ClaudeWindows)
            {
                var w = c[key];
                if (w is null) continue;
                var pct = w.Num("utilization", -1);
                if (pct < 0) continue;
                var resets = DateTimeOffset.TryParse(w.Str("resets_at"), out var r) ? r : (DateTimeOffset?)null;
                windows.Add(Window("claude", id, label, pct, resets));
            }
        return new JsonObject
        {
            ["provider"] = "claude", ["name"] = "Claude", ["plan"] = c.Str("_plan"),
            ["windows"] = windows, ["error"] = c is null ? claudeError ?? "Carregando…" : claudeError,
            ["updated"] = c is null ? null : claudeAt.ToUnixTimeMilliseconds(),
        };
    }

    JsonObject CodexJson()
    {
        var windows = new JsonArray();
        var rl = sessions.CodexLimits;
        var at = sessions.CodexLimitsAt;
        foreach (var (key, id) in new[] { ("primary", "session"), ("secondary", "weekly") })
        {
            var w = rl?[key];
            if (w is null) continue;
            var pct = w.Num("used_percent", -1);
            if (pct < 0) continue;
            DateTimeOffset? resets = null;
            if (w.Num("resets_at") is > 0 and var ra) resets = DateTimeOffset.FromUnixTimeSeconds((long)ra);
            else if (w.Num("resets_in_seconds", -1) is >= 0 and var rin) resets = at.AddSeconds(rin);
            if (resets is DateTimeOffset rr && rr < DateTimeOffset.UtcNow) pct = 0; // window rolled over since Codex last wrote it
            var mins = w.Num("window_minutes");
            var label = id == "session" ? (mins > 0 && mins < 1440 ? $"Sessão ({mins / 60:0} h)" : "Sessão") : "Semanal";
            windows.Add(Window("codex", id, label, pct, resets));
        }
        return new JsonObject
        {
            ["provider"] = "codex", ["name"] = "Codex", ["windows"] = windows,
            ["error"] = rl is null ? "Nenhuma sessão do Codex nas últimas 24 h." : null,
            ["updated"] = rl is null ? null : at.ToUnixTimeMilliseconds(),
        };
    }

    /// <summary>One window with a linear burn-rate forecast over the last 60 minutes.</summary>
    JsonObject Window(string provider, string id, string label, double pct, DateTimeOffset? resets)
    {
        var key = provider + ":" + id;
        var now = DateTimeOffset.UtcNow;
        long? fullAt = null;
        lock (gate)
        {
            if (!samples.TryGetValue(key, out var s)) samples[key] = s = new();
            if (s.Count > 0 && pct + 1 < s[^1].pct) s.Clear(); // the window reset
            if (s.Count == 0 || (now - s[^1].t).TotalSeconds >= 60 || Math.Abs(s[^1].pct - pct) > 0.01) s.Add((now, pct));
            s.RemoveAll(x => (now - x.t).TotalMinutes > 60);
            if (s.Count >= 2)
            {
                var first = s[0];
                var hours = (now - first.t).TotalHours;
                var rate = hours > 0.05 ? (pct - first.pct) / hours : 0; // % per hour
                if (rate > 0.2 && pct < 100)
                {
                    var eta = now.AddHours((100 - pct) / rate);
                    if (resets is null || eta < resets) fullAt = eta.ToUnixTimeMilliseconds();
                }
            }
        }
        return new JsonObject
        {
            ["id"] = id, ["label"] = label, ["pct"] = Math.Round(pct, 1),
            ["resetsAt"] = resets?.ToUnixTimeMilliseconds(), ["fullAt"] = fullAt,
        };
    }

    public void Dispose() => cts.Cancel();
}
