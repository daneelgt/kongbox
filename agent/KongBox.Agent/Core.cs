using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>Embedded files (default config, catalog, templates, settings page, the panel).</summary>
static class Resources
{
    static readonly Assembly Asm = typeof(Resources).Assembly;

    public static Stream? Open(string name) => Asm.GetManifestResourceStream(name);

    public static string Text(string name)
    {
        using var s = Open(name) ?? throw new FileNotFoundException(name);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static byte[]? Bytes(string name)
    {
        using var s = Open(name);
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>Small rolling log in %APPDATA%\KongBox\agent.log.</summary>
static class Log
{
    static readonly object Gate = new();
    static string PathName => System.IO.Path.Combine(AppPaths.Dir, "agent.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);

    static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(PathName);
                if (fi.Exists && fi.Length > 1_000_000) File.Move(PathName, PathName + ".1", true);
                File.AppendAllText(PathName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {msg}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the agent */ }
    }
}

static class AppPaths
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KongBox");
    public static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static AppPaths() => Directory.CreateDirectory(Dir);
    public static string File(string name) => Path.Combine(Dir, name);
}

static class Json
{
    public static readonly JsonDocumentOptions Doc = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonNode? Parse(string text) => JsonNode.Parse(text, null, Doc);

    public static string Str(this JsonNode? n, string key, string def = "")
    {
        try { var v = n?[key]; return v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : def; } catch { return def; }
    }

    public static double Num(this JsonNode? n, string key, double def = 0)
    {
        try
        {
            var v = n?[key];
            if (v is not JsonValue jv) return def;
            // values built in code keep their CLR type (a long stays a long), parsed ones convert freely
            if (jv.TryGetValue<double>(out var d)) return d;
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<float>(out var f)) return f;
            if (jv.TryGetValue<decimal>(out var m)) return (double)m;
            if (jv.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return def;
        }
        catch { return def; }
    }

    public static bool Bool(this JsonNode? n, string key, bool def = false)
    {
        try { var v = n?[key]; return v is JsonValue jv && jv.TryGetValue<bool>(out var b) ? b : def; } catch { return def; }
    }

    public static JsonArray Arr(this JsonNode? n, string key) => n?[key] as JsonArray ?? new JsonArray();
}

/// <summary>Fixed-size history for charts (one sample per second).</summary>
sealed class Ring(int size)
{
    readonly double[] buf = new double[size];
    int head, count;
    readonly object gate = new();

    public void Add(double v)
    {
        lock (gate) { buf[head] = Math.Round(v, 1); head = (head + 1) % buf.Length; if (count < buf.Length) count++; }
    }

    public double[] ToArray()
    {
        lock (gate)
        {
            var r = new double[count];
            for (int i = 0; i < count; i++) r[i] = buf[(head - count + i + buf.Length) % buf.Length];
            return r;
        }
    }
}
