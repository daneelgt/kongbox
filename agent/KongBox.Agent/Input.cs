using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using NAudio.CoreAudioApi;

namespace KongBox.Agent;

/// <summary>Master volume and mute of the default output (Core Audio).</summary>
static class AudioControl
{
    static readonly object Gate = new();

    static T With<T>(Func<AudioEndpointVolume, T> f, T fallback)
    {
        try
        {
            lock (Gate)
            {
                using var en = new MMDeviceEnumerator();
                using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return f(dev.AudioEndpointVolume);
            }
        }
        catch { return fallback; }
    }

    public static JsonObject? State() => With<JsonObject?>(v => new JsonObject
    {
        ["level"] = (int)Math.Round(v.MasterVolumeLevelScalar * 100),
        ["muted"] = v.Mute,
    }, null);

    public static bool Set(int? level, bool? muted) => With(v =>
    {
        if (level is int l) v.MasterVolumeLevelScalar = Math.Clamp(l, 0, 100) / 100f;
        if (muted is bool m) v.Mute = m;
        return true;
    }, false);

    public static bool ToggleMute() => With(v => { v.Mute = !v.Mute; return true; }, false);
    public static bool IsMuted() => With(v => v.Mute, false);
}

/// <summary>Key combinations ("ctrl+shift+m"), typed text and media keys via SendInput.</summary>
static class KeySender
{
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    const uint INPUT_KEYBOARD = 1, KEYUP = 0x2, UNICODE = 0x4, EXTENDED = 0x1;

    static readonly Dictionary<string, ushort> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["win"] = 0x5B, ["cmd"] = 0x5B, ["meta"] = 0x5B,
        ["enter"] = 0x0D, ["return"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B, ["tab"] = 0x09, ["space"] = 0x20,
        ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22, ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["printscreen"] = 0x2C, ["capslock"] = 0x14,
        ["volumeup"] = 0xAF, ["volumedown"] = 0xAE, ["volumemute"] = 0xAD,
        ["playpause"] = 0xB3, ["nexttrack"] = 0xB0, ["prevtrack"] = 0xB1, ["stop"] = 0xB2,
        ["plus"] = 0xBB, ["minus"] = 0xBD, ["comma"] = 0xBC, ["period"] = 0xBE,
    };

    static readonly HashSet<ushort> Extended = [0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22, 0x26, 0x28, 0x25, 0x27, 0x5B, 0xAF, 0xAE, 0xAD, 0xB3, 0xB0, 0xB1, 0xB2];

    static ushort? Vk(string k)
    {
        k = k.Trim();
        if (Keys.TryGetValue(k, out var v)) return v;
        if (k.Length == 1)
        {
            char c = char.ToUpperInvariant(k[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (k.Length >= 2 && (k[0] == 'f' || k[0] == 'F') && int.TryParse(k[1..], out var f) && f is >= 1 and <= 24) return (ushort)(0x70 + f - 1);
        return null;
    }

    static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? KEYUP : 0) | (Extended.Contains(vk) ? EXTENDED : 0) } },
    };

    /// <summary>"ctrl+shift+m" → press in order, release in reverse.</summary>
    public static bool Combo(string combo)
    {
        var vks = new List<ushort>();
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Vk(part) is not ushort vk) return false;
            vks.Add(vk);
        }
        if (vks.Count == 0) return false;
        var inputs = vks.Select(v => Key(v, false)).Concat(Enumerable.Reverse(vks).Select(v => Key(v, true))).ToArray();
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    public static bool Type(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n') { inputs.Add(Key(0x0D, false)); inputs.Add(Key(0x0D, true)); continue; }
            foreach (var up in new[] { false, true })
                inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = UNICODE | (up ? KEYUP : 0) } } });
        }
        if (inputs.Count == 0) return true;
        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>()) == inputs.Count;
    }

    public static bool MediaKey(string action) => action switch
    {
        "play-pause" or "toggle" => Combo("playpause"),
        "next" => Combo("nexttrack"),
        "prev" => Combo("prevtrack"),
        _ => false,
    };
}
