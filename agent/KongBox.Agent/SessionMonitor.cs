using System.Text;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>
/// Local Claude Code (~/.claude/projects/**/*.jsonl) and Codex (~/.codex/sessions/**/rollout-*.jsonl) sessions.
/// Files are read incrementally (only the bytes appended since the last pass). Prompt and response text is never kept.
/// </summary>
sealed class SessionMonitor : IDisposable
{
    sealed class Sess
    {
        public string Id = "", Tool = "", File = "", Project = "", Model = "", Branch = "", LastKind = "", Cwd = "";
        public long Offset;
        public DateTimeOffset Start, Last;
        public long Input, Output, CacheRead, CacheWrite;
        public int Turns, Tools;
        public string Partial = "";
    }

    readonly CancellationTokenSource cts = new();
    readonly Dictionary<string, Sess> sessions = new();
    readonly object gate = new();
    public JsonObject? CodexLimits { get; private set; }
    public DateTimeOffset CodexLimitsAt { get; private set; }

    static readonly string ClaudeDir = Path.Combine(AppPaths.Home, ".claude", "projects");
    static readonly string CodexDir = Path.Combine(AppPaths.Home, ".codex", "sessions");

    public SessionMonitor() => Task.Run(() => Loop(cts.Token));

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Scan(); } catch (Exception e) { Log.Warn("sessions: " + e.Message); }
            try { await Task.Delay(3000, ct); } catch { break; }
        }
    }

    void Scan()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var files = new List<(string path, string tool)>();
        if (Directory.Exists(ClaudeDir))
            foreach (var f in Directory.EnumerateFiles(ClaudeDir, "*.jsonl", SearchOption.AllDirectories))
                if (File.GetLastWriteTimeUtc(f) > cutoff) files.Add((f, "claude"));
        if (Directory.Exists(CodexDir))
        {
            // only the last 3 day folders: YYYY/MM/DD
            foreach (var f in Directory.EnumerateFiles(CodexDir, "rollout-*.jsonl", SearchOption.AllDirectories))
                if (File.GetLastWriteTimeUtc(f) > cutoff) files.Add((f, "codex"));
        }
        foreach (var (path, tool) in files.OrderByDescending(x => File.GetLastWriteTimeUtc(x.path)).Take(30))
        {
            Sess s;
            lock (gate)
            {
                if (!sessions.TryGetValue(path, out s!))
                {
                    s = new Sess { File = path, Tool = tool, Id = Path.GetFileNameWithoutExtension(path) };
                    if (tool == "claude") s.Project = DecodeClaudeProject(Path.GetFileName(Path.GetDirectoryName(path) ?? ""));
                    sessions[path] = s;
                }
            }
            ReadNew(s);
        }
        lock (gate)
            foreach (var k in sessions.Where(kv => kv.Value.Last.UtcDateTime < cutoff && kv.Value.Last != default).Select(kv => kv.Key).ToList())
                sessions.Remove(k);
    }

    static string DecodeClaudeProject(string folder)
    {
        // "D--Documentos-kongbox" → "kongbox" (the cwd field, when present, wins)
        var parts = folder.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : folder;
    }

    void ReadNew(Sess s)
    {
        try
        {
            using var fs = new FileStream(s.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < s.Offset) { s.Offset = 0; s.Partial = ""; }
            if (fs.Length == s.Offset) return;
            // a huge first read: skip to the last 4 MB (older turns only matter for totals)
            if (s.Offset == 0 && fs.Length > 4_000_000) s.Offset = fs.Length - 4_000_000;
            fs.Seek(s.Offset, SeekOrigin.Begin);
            var buf = new byte[fs.Length - s.Offset];
            int read = 0;
            while (read < buf.Length) { var n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
            s.Offset += read;
            var text = s.Partial + Encoding.UTF8.GetString(buf, 0, read);
            var lines = text.Split('\n');
            s.Partial = lines[^1];
            for (int i = 0; i < lines.Length - 1; i++)
            {
                var line = lines[i].Trim();
                if (line.Length < 2 || line[0] != '{') continue;
                JsonNode? n;
                try { n = JsonNode.Parse(line); } catch { continue; }
                lock (gate) { if (s.Tool == "claude") Claude(s, n); else Codex(s, n); }
            }
        }
        catch (IOException) { /* being written */ }
    }

    static DateTimeOffset Time(JsonNode? n, string key = "timestamp") =>
        DateTimeOffset.TryParse(n.Str(key), out var t) ? t : default;

    static void Touch(Sess s, DateTimeOffset t)
    {
        if (t == default) return;
        if (s.Start == default || t < s.Start) s.Start = t;
        if (t > s.Last) s.Last = t;
    }

    static void Claude(Sess s, JsonNode? n)
    {
        var type = n.Str("type");
        if (type is not ("user" or "assistant")) return;
        Touch(s, Time(n));
        if (n.Str("cwd") is { Length: > 0 } cwd) { s.Project = Path.GetFileName(cwd.TrimEnd('\\', '/')); s.Cwd = cwd; }
        if (n.Str("gitBranch") is { Length: > 0 } br) s.Branch = br;
        var msg = n?["message"];
        if (type == "assistant")
        {
            if (msg.Str("model") is { Length: > 0 } m && !m.StartsWith('<')) s.Model = m;
            var u = msg?["usage"];
            if (u is not null)
            {
                s.Input += (long)u.Num("input_tokens");
                s.Output += (long)u.Num("output_tokens");
                s.CacheRead += (long)u.Num("cache_read_input_tokens");
                s.CacheWrite += (long)u.Num("cache_creation_input_tokens");
            }
            var stop = msg.Str("stop_reason");
            bool tool = false;
            foreach (var c in msg.Arr("content")) if (c.Str("type") == "tool_use") { tool = true; s.Tools++; }
            s.LastKind = tool || stop == "tool_use" ? "tool" : stop == "end_turn" ? "done" : "assistant";
        }
        else
        {
            // a real prompt (string content) counts as a turn; tool results do not
            var content = msg?["content"];
            bool isToolResult = content is JsonArray arr && arr.Any(c => c.Str("type") == "tool_result");
            if (!isToolResult && !n.Bool("isMeta")) s.Turns++;
            s.LastKind = isToolResult ? "tool-result" : "prompt";
        }
    }

    void Codex(Sess s, JsonNode? n)
    {
        var t = Time(n);
        Touch(s, t);
        var type = n.Str("type");
        var p = n?["payload"];
        switch (type)
        {
            case "session_meta":
                if (p.Str("cwd") is { Length: > 0 } cwd) { s.Project = Path.GetFileName(cwd.TrimEnd('\\', '/')); s.Cwd = cwd; }
                if (p.Str("id") is { Length: > 0 } id) s.Id = id;
                break;
            case "turn_context":
                if (p.Str("model") is { Length: > 0 } m) s.Model = m;
                if (p.Str("cwd") is { Length: > 0 } c2) s.Project = Path.GetFileName(c2.TrimEnd('\\', '/'));
                break;
            case "event_msg":
                switch (p.Str("type"))
                {
                    case "user_message": s.Turns++; s.LastKind = "prompt"; break;
                    case "agent_message": s.LastKind = "assistant"; break;
                    case "task_complete": s.LastKind = "done"; break;
                    case "exec_command_begin": s.Tools++; s.LastKind = "tool"; break;
                    case "token_count":
                        var tot = p?["info"]?["total_token_usage"];
                        if (tot is not null)
                        {
                            s.Input = (long)tot.Num("input_tokens");
                            s.Output = (long)tot.Num("output_tokens");
                            s.CacheRead = (long)tot.Num("cached_input_tokens");
                        }
                        if (p?["rate_limits"] is JsonObject rl && t >= CodexLimitsAt)
                        {
                            CodexLimits = (JsonObject)rl.DeepClone();
                            CodexLimitsAt = t;
                        }
                        break;
                }
                break;
        }
    }

    static string State(Sess s)
    {
        var idle = DateTimeOffset.UtcNow - s.Last;
        if (idle.TotalMinutes > 15) return "idle";
        return s.LastKind switch
        {
            "done" => "waiting",
            "prompt" or "tool" or "tool-result" => idle.TotalMinutes < 5 ? "working" : "idle",
            _ => idle.TotalMinutes < 2 ? "working" : "waiting",
        };
    }

    public JsonArray ToJson(int limit = 12)
    {
        lock (gate)
        {
            return new JsonArray(sessions.Values.Where(s => s.Last != default).OrderByDescending(s => s.Last).Take(limit).Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id,
                ["tool"] = s.Tool,
                ["project"] = s.Project,
                ["branch"] = s.Branch,
                ["canOpen"] = s.Cwd != "" && Directory.Exists(s.Cwd),
                ["model"] = s.Model,
                ["state"] = State(s),
                ["start"] = s.Start.ToUnixTimeMilliseconds(),
                ["last"] = s.Last.ToUnixTimeMilliseconds(),
                ["turns"] = s.Turns,
                ["tools"] = s.Tools,
                ["tokens"] = new JsonObject { ["in"] = s.Input, ["out"] = s.Output, ["cacheRead"] = s.CacheRead, ["cacheWrite"] = s.CacheWrite },
            }).ToArray());
        }
    }

    /// <summary>Folders that hold your projects: the parents of every session's working directory.</summary>
    public IEnumerable<string> Roots()
    {
        lock (gate)
            return sessions.Values.Where(s => s.Cwd != "").Select(s => Path.GetDirectoryName(s.Cwd.TrimEnd('\\', '/')) ?? "")
                .Where(d => d != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Opens a session's project folder in VS Code (the phone only sends the session id).</summary>
    public (bool ok, string msg) OpenInEditor(string id)
    {
        string? cwd;
        lock (gate) cwd = sessions.Values.FirstOrDefault(s => s.Id == id)?.Cwd;
        if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd)) return (false, "Pasta da sessão não encontrada.");
        // "code" is a .cmd on Windows: start it through cmd, hidden, with the folder quoted
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c code \"{cwd}\"") { UseShellExecute = false, CreateNoWindow = true };
        System.Diagnostics.Process.Start(psi)?.Dispose();
        return (true, "Abrindo " + Path.GetFileName(cwd) + " no VS Code.");
    }

    public void Dispose() => cts.Cancel();
}
