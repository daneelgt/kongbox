using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>
/// Claude Code permission prompts on the phone. Claude Code runs "KongBoxAgent.exe --claude-hook" as a
/// PermissionRequest hook; that process forwards the request here and waits for Allow / Deny from the phone.
/// No answer in time → no decision → Claude Code asks in the terminal as usual.
/// </summary>
sealed class ApprovalStore
{
    public sealed class Pending
    {
        public required string Id;
        public required JsonObject Info;
        public readonly TaskCompletionSource<bool?> Decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    readonly ConcurrentDictionary<string, Pending> pending = new();

    public async Task<bool?> Ask(JsonNode? hook, TimeSpan timeout)
    {
        var tool = hook.Str("tool_name", "ferramenta");
        var input = hook?["tool_input"];
        string detail = input.Str("command") is { Length: > 0 } cmd ? cmd
            : input.Str("file_path") is { Length: > 0 } fp ? fp
            : input.Str("url") is { Length: > 0 } url ? url
            : input?.ToJsonString() ?? "";
        if (detail.Length > 600) detail = detail[..600] + "…";
        var cwd = hook.Str("cwd");
        var p = new Pending
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Info = new JsonObject
            {
                ["project"] = cwd == "" ? "" : Path.GetFileName(cwd.TrimEnd('\\', '/')),
                ["tool"] = tool,
                ["detail"] = detail,
                ["created"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["expires"] = DateTimeOffset.UtcNow.Add(timeout).ToUnixTimeMilliseconds(),
            },
        };
        p.Info["id"] = p.Id;
        pending[p.Id] = p;
        try
        {
            var done = await Task.WhenAny(p.Decision.Task, Task.Delay(timeout));
            return done == p.Decision.Task ? p.Decision.Task.Result : null;
        }
        finally { pending.TryRemove(p.Id, out _); }
    }

    public bool Answer(string id, bool allow) => pending.TryGetValue(id, out var p) && p.Decision.TrySetResult(allow);

    public JsonArray ToJson() => new(pending.Values.OrderBy(p => p.Info.Num("created")).Select(p => (JsonNode)p.Info.DeepClone()).ToArray());
}

static class ClaudeHook
{
    static string SettingsPath => Path.Combine(AppPaths.Home, ".claude", "settings.json");
    const string Marker = "--claude-hook";

    static string Command => $"\"{Environment.ProcessPath}\" {Marker}";

    public static bool Installed()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            return File.ReadAllText(SettingsPath).Contains("KongBoxAgent", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Adds the PermissionRequest hook, keeping a backup next to settings.json.</summary>
    public static void Install()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        JsonObject root = new();
        if (File.Exists(SettingsPath))
        {
            File.Copy(SettingsPath, SettingsPath + ".kongbox.bak", true);
            root = Json.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject();
        }
        Remove(root);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        var list = hooks["PermissionRequest"] as JsonArray ?? new JsonArray();
        hooks["PermissionRequest"] = list;
        list.Add(new JsonObject
        {
            ["matcher"] = "*",
            ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command, ["timeout"] = 60 }),
        });
        File.WriteAllText(SettingsPath, root.ToJsonString(Json.Pretty));
    }

    public static void Uninstall()
    {
        if (!File.Exists(SettingsPath)) return;
        var root = Json.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
        if (root is null) return;
        File.Copy(SettingsPath, SettingsPath + ".kongbox.bak", true);
        Remove(root);
        File.WriteAllText(SettingsPath, root.ToJsonString(Json.Pretty));
    }

    static void Remove(JsonObject root)
    {
        if (root["hooks"]?["PermissionRequest"] is not JsonArray list) return;
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i]?.ToJsonString().Contains("KongBoxAgent", StringComparison.OrdinalIgnoreCase) == true) list.RemoveAt(i);
        if (list.Count == 0) (root["hooks"] as JsonObject)!.Remove("PermissionRequest");
    }

    /// <summary>Hook mode: stdin → agent → stdout. Prints nothing when there is no decision.</summary>
    public static async Task<int> RunHookProcess()
    {
        try
        {
            using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            var body = await stdin.ReadToEndAsync();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(58) };
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:8765/api/claude/hook") { Content = content };
            req.Headers.Add("X-Kong", "1");
            using var res = await http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            var decision = Json.Parse(text).Str("decision");
            if (decision is "allow" or "deny")
            {
                var output = new JsonObject
                {
                    ["hookSpecificOutput"] = new JsonObject
                    {
                        ["hookEventName"] = "PermissionRequest",
                        ["decision"] = decision == "allow"
                            ? new JsonObject { ["behavior"] = "allow" }
                            : new JsonObject { ["behavior"] = "deny", ["message"] = "Negado pelo painel KONGBOX." },
                    },
                };
                using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                await stdout.WriteAsync(output.ToJsonString());
            }
        }
        catch { /* agent off → Claude Code asks in the terminal */ }
        return 0;
    }
}
