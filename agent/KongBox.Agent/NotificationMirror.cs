using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace KongBox.Agent;

/// <summary>
/// Mirrors Windows toast notifications (WhatsApp, Discord, mail…) to the panel. Unpackaged apps cannot use the
/// official listener API, so this reads a copy of the Action Center database every 2 s when it changes.
/// Only new toasts are forwarded; nothing that arrived before the agent started is replayed.
/// </summary>
sealed class NotificationMirror : IDisposable
{
    static readonly string Db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications", "wpndatabase.db");
    readonly ConfigStore config;
    readonly PushStore push;
    readonly CancellationTokenSource cts = new();
    readonly string copyDir = Path.Combine(Path.GetTempPath(), "kongbox-wpn");
    long lastId = -1;
    DateTime lastStamp = DateTime.MinValue;
    public string? Error { get; private set; }
    public bool Available => File.Exists(Db);

    public NotificationMirror(ConfigStore config, PushStore push)
    {
        (this.config, this.push) = (config, push);
        Directory.CreateDirectory(copyDir);
        Task.Run(() => Loop(cts.Token));
    }

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { if (config.Snapshot()["notifications"].Bool("windows", true)) Poll(); else lastId = -1; }
            catch (Exception e) { Error = e.Message; Log.Warn("notifications: " + e.Message); }
            try { await Task.Delay(2000, ct); } catch { break; }
        }
    }

    void Poll()
    {
        if (!File.Exists(Db)) { Error = "Banco de notificações do Windows não encontrado."; return; }
        // the database is written through its WAL file; skip the copy when neither changed
        var stamp = new[] { Db, Db + "-wal" }.Where(File.Exists).Select(File.GetLastWriteTimeUtc).Max();
        if (stamp == lastStamp && lastId >= 0) return;
        lastStamp = stamp;

        var copy = Path.Combine(copyDir, "wpn.db");
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var src = Db + ext;
            var dst = copy + ext;
            if (File.Exists(src))
            {
                using var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var o = new FileStream(dst, FileMode.Create, FileAccess.Write);
                i.CopyTo(o);
            }
            else if (File.Exists(dst)) File.Delete(dst);
        }

        // no pooling: a pooled connection keeps the copy open and the next poll could not overwrite it
        using var conn = new SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        if (lastId < 0)
        {
            // first pass: remember where we are, forward nothing old
            cmd.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM Notification";
            lastId = Convert.ToInt64(cmd.ExecuteScalar());
            Error = null;
            return;
        }
        cmd.CommandText = "SELECT n.Id, n.Payload, h.PrimaryId FROM Notification n JOIN NotificationHandler h ON n.HandlerId = h.RecordId " +
                          "WHERE n.Type = 'toast' AND n.Id > $last ORDER BY n.Id LIMIT 20";
        cmd.Parameters.AddWithValue("$last", lastId);
        var ignore = config.Snapshot()["notifications"].Arr("ignore").Select(x => x?.ToString() ?? "").Where(x => x != "").ToList();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            lastId = Math.Max(lastId, r.GetInt64(0));
            var aumid = r.IsDBNull(2) ? "" : r.GetString(2);
            if (aumid.StartsWith("KongBox", StringComparison.OrdinalIgnoreCase)) continue;
            var payload = r.IsDBNull(1) ? "" : System.Text.Encoding.UTF8.GetString((byte[])r.GetValue(1));
            var (title, body) = Parse(payload);
            if (title == "" && body == "") continue;
            var app = AppName(aumid);
            if (ignore.Any(x => app.Contains(x, StringComparison.OrdinalIgnoreCase) || aumid.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
            push.Notify(new JsonObject
            {
                ["title"] = title == "" ? app : title,
                ["text"] = body,
                ["app"] = app,
                ["aumid"] = aumid,
                ["source"] = "windows",
                ["level"] = "info",
            });
        }
        Error = null;
    }

    /// <summary>First text of the toast is the title, the rest is the body (attribution text skipped).</summary>
    static (string title, string body) Parse(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var texts = doc.Descendants().Where(e => e.Name.LocalName == "text" && (string?)e.Attribute("placement") != "attribution")
                           .Select(e => e.Value.Trim()).Where(t => t != "").ToList();
            if (texts.Count == 0) return ("", "");
            var body = string.Join(" · ", texts.Skip(1));
            return (Cut(texts[0], 120), Cut(body, 400));
        }
        catch { return ("", ""); }
    }

    static string Cut(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    static readonly (string key, string name)[] Known =
    [
        ("whatsapp", "WhatsApp"), ("telegram", "Telegram"), ("discord", "Discord"), ("slack", "Slack"), ("teams", "Teams"),
        ("outlook", "Outlook"), ("microsoft.windowscommunicationsapps", "Email"), ("spotify", "Spotify"), ("chrome", "Chrome"),
        ("msedge", "Edge"), ("firefox", "Firefox"), ("claude", "Claude"), ("steam", "Steam"), ("instagram", "Instagram"),
        ("windows.defender", "Segurança do Windows"), ("windowsterminal", "Terminal"), ("vscode", "VS Code"), ("code", "VS Code"),
    ];

    public static string AppName(string aumid)
    {
        var a = aumid.ToLowerInvariant();
        foreach (var (key, name) in Known) if (a.Contains(key)) return name;
        // desktop apps registered by path ("{GUID}\…\powershell.exe") → the executable's name
        if (aumid.Contains('\\') || aumid.Contains('/'))
        {
            var exe = Path.GetFileNameWithoutExtension(aumid.Replace('/', '\\').Split('\\').Last());
            if (exe.Length > 0) return char.ToUpperInvariant(exe[0]) + exe[1..];
        }
        // "Publisher.AppName_hash!App" → "AppName"
        var core = aumid.Split('!')[0].Split('_')[0];
        var last = core.Split('.').LastOrDefault(x => x.Length > 1) ?? core;
        return last.Length > 0 ? last : "Windows";
    }

    /// <summary>Opens the app that sent a toast (works for Store apps and registered desktop apps).</summary>
    public static bool OpenApp(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid) || aumid.IndexOfAny(['"', '\r', '\n']) >= 0) return false;
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + aumid) { UseShellExecute = true })?.Dispose();
        return true;
    }

    public void Dispose() => cts.Cancel();
}
