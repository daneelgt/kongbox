using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>Runs configured shortcuts by id: programs, URLs, built-ins, key combos, text and macros.</summary>
sealed class Shortcuts(ConfigStore config, Func<MediaMonitor> media)
{
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public JsonNode? Find(string id) => config.Snapshot().Arr("shortcuts").FirstOrDefault(s => s.Str("id") == id);

    public async Task<(bool ok, string msg)> Run(string id, int depth = 0)
    {
        if (depth > 4) return (false, "Macro aninhada demais.");
        var s = Find(id);
        if (s is null) return (false, $"Atalho \"{id}\" não existe.");
        return await Execute(s, depth);
    }

    public async Task<(bool ok, string msg)> Execute(JsonNode s, int depth = 0)
    {
        try
        {
            if (s["macro"] is JsonArray macro)
            {
                foreach (var step in macro)
                {
                    if (step.Str("keys") is { Length: > 0 } k) KeySender.Combo(k);
                    else if (step.Str("text") is { Length: > 0 } t) KeySender.Type(t);
                    else if (step.Num("wait") is > 0 and var w) await Task.Delay((int)Math.Min(w, 10_000));
                    else if (step.Str("run") is { Length: > 0 } r) await Run(r, depth + 1);
                    await Task.Delay(40);
                }
                return (true, "Macro executada.");
            }
            if (s.Str("keys") is { Length: > 0 } keys) return KeySender.Combo(keys) ? (true, "Teclas enviadas.") : (false, $"Combinação inválida: {keys}");
            if (s.Str("text") is { Length: > 0 } text) return KeySender.Type(text) ? (true, "Texto digitado.") : (false, "Falha ao digitar.");
            var cmd = s.Str("cmd").Trim();
            if (cmd == "") return (false, "Atalho sem comando.");
            if (cmd.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase)) return await Builtin(cmd[8..].ToLowerInvariant());
            Launch(cmd, s.Str("args"));
            return (true, "Aberto.");
        }
        catch (Exception e) { return (false, e.Message); }
    }

    async Task<(bool, string)> Builtin(string name)
    {
        switch (name)
        {
            case "lock": return (LockWorkStation(), "Bloqueado.");
            case "monitor-off":
                SendMessage((IntPtr)0xFFFF, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)0xF170 /* SC_MONITORPOWER */, (IntPtr)2);
                return (true, "Monitor desligado.");
            case "mute": return (AudioControl.ToggleMute(), AudioControl.IsMuted() ? "Mudo." : "Som ligado.");
            case "play-pause": case "next": case "prev": return (await media().Action(name), "Ok.");
            case "volume-up": return (KeySender.Combo("volumeup"), "Ok.");
            case "volume-down": return (KeySender.Combo("volumedown"), "Ok.");
            default: return (false, $"builtin desconhecido: {name}");
        }
    }

    /// <summary>URL/shell:/ms-settings: addresses and plain programs, with or without arguments.</summary>
    static void Launch(string cmd, string args)
    {
        var looksLikeUri = cmd.Contains(':') && !System.IO.Path.IsPathRooted(cmd);
        if (!looksLikeUri && args == "" && !File.Exists(cmd))
        {
            // "code --new-window" → program + args (quoted paths stay whole)
            if (cmd.StartsWith('"'))
            {
                var end = cmd.IndexOf('"', 1);
                if (end > 0) { args = cmd[(end + 1)..].Trim(); cmd = cmd[1..end]; }
            }
            else if (cmd.Contains(' '))
            {
                var i = cmd.IndexOf(' ');
                args = cmd[(i + 1)..].Trim();
                cmd = cmd[..i];
            }
        }
        Process.Start(new ProcessStartInfo(cmd, args) { UseShellExecute = true })?.Dispose();
    }
}
