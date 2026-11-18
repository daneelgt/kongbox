using System.Diagnostics;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>
/// Keeps "adb reverse tcp:8765 tcp:8765" in place on every connected phone, so the phone's 127.0.0.1:8765 is this
/// agent over USB. Also installs and opens the panel app.
/// </summary>
sealed class AdbReverse : IDisposable
{
    public const string Package = "dev.kongbox.panel";
    readonly ConfigStore config;
    readonly CancellationTokenSource cts = new();
    readonly object gate = new();
    List<JsonObject> phones = new();
    public string? AdbPath { get; private set; }
    public string? Error { get; private set; }

    public AdbReverse(ConfigStore config)
    {
        this.config = config;
        Task.Run(() => Loop(cts.Token));
    }

    public JsonArray Phones() { lock (gate) return new JsonArray(phones.Select(p => (JsonNode)p.DeepClone()).ToArray()); }

    string? FindAdb()
    {
        var configured = config.Snapshot().Str("adbPath");
        var candidates = new List<string>();
        if (configured != "") candidates.Add(configured);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir.Trim('"'), "adb.exe"));
        foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            if (Environment.GetEnvironmentVariable(env) is { Length: > 0 } sdk) candidates.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));
        candidates.Add(@"E:\DevTools\android-sdk\platform-tools\adb.exe");
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public (int code, string output) Adb(string args, int timeoutMs = 8000)
    {
        AdbPath ??= FindAdb();
        if (AdbPath is null) return (-1, "adb não encontrado");
        var psi = new ProcessStartInfo(AdbPath, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-2, "adb não respondeu"); }
        return (p.ExitCode, (outTask.Result + errTask.Result).Trim());
    }

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); } catch (Exception e) { Error = e.Message; }
            try { await Task.Delay(5000, ct); } catch { break; }
        }
    }

    void Tick()
    {
        AdbPath ??= FindAdb();
        if (AdbPath is null) { Error = "adb não encontrado (instale as platform-tools ou defina adbPath)."; lock (gate) phones = new(); return; }
        var (code, output) = Adb("devices -l");
        if (code != 0) { Error = output; return; }
        Error = null;
        var only = config.Snapshot().Str("deviceSerial");
        var list = new List<JsonObject>();
        foreach (var line in output.Split('\n').Skip(1))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var serial = parts[0];
            var status = parts[1];
            if (only != "" && serial != only) continue;
            var model = parts.FirstOrDefault(x => x.StartsWith("model:"))?[6..].Replace('_', ' ') ?? serial;
            var phone = new JsonObject { ["serial"] = serial, ["status"] = status, ["model"] = model, ["reverse"] = false, ["installed"] = false };
            if (status == "device")
            {
                var (_, rev) = Adb($"-s {serial} reverse --list");
                if (!rev.Contains("tcp:8765")) Adb($"-s {serial} reverse tcp:8765 tcp:8765");
                phone["reverse"] = true;
                var (_, pkgs) = Adb($"-s {serial} shell pm list packages {Package}");
                phone["installed"] = pkgs.Contains(Package);
            }
            list.Add(phone);
        }
        lock (gate) phones = list;
    }

    public static string? FindApk()
    {
        foreach (var p in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "KongBox.apk"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "android", "app", "build", "outputs", "apk", "debug", "app-debug.apk")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "android", "app", "build", "outputs", "apk", "release", "app-release.apk")),
        })
            if (File.Exists(p)) return p;
        return null;
    }

    public (bool ok, string msg) Install(string serial)
    {
        var apk = ApkShare.ApkPath();
        if (apk is null) return (false, "Este agente não tem o APK (rode o build de release).");
        var (code, output) = Adb($"-s {serial} install -r \"{apk}\"", 120_000);
        return (code == 0 && output.Contains("Success"), output.Split('\n').LastOrDefault() ?? output);
    }

    public (bool ok, string msg) Open(string serial)
    {
        var (code, output) = Adb($"-s {serial} shell am start -n {Package}/.MainActivity");
        return (code == 0, output);
    }

    public void Dispose() => cts.Cancel();
}
