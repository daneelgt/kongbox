using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>CPU, memory, disks, network, ping and GPU, sampled once a second with 2 minutes of history.</summary>
sealed class SystemMonitor : IDisposable
{
    const int HistorySize = 120;
    public readonly Dictionary<string, Ring> History = new()
    {
        ["cpu"] = new(HistorySize), ["gpu"] = new(HistorySize), ["ram"] = new(HistorySize),
        ["down"] = new(HistorySize), ["up"] = new(HistorySize), ["ping"] = new(HistorySize),
        ["vram"] = new(HistorySize), ["disk"] = new(HistorySize), ["gputemp"] = new(HistorySize),
    };

    readonly ConfigStore config;
    readonly CancellationTokenSource cts = new();
    readonly object gate = new();
    JsonObject snapshot = new();

    // CPU
    ulong lastIdle, lastKernel, lastUser;
    // network
    long lastRx = -1, lastTx = -1;
    DateTime lastNetAt;
    // ping
    double? pingMs;
    DateTime lastPing = DateTime.MinValue;
    // GPU
    readonly GpuReader gpu = new();

    public SystemMonitor(ConfigStore config)
    {
        this.config = config;
        Task.Run(() => Loop(cts.Token));
    }

    public JsonObject Snapshot() { lock (gate) return (JsonObject)snapshot.DeepClone(); }

    /// <summary>Latest value of a named metric (for rules and stat tiles).</summary>
    public double? Metric(string name)
    {
        var h = History.TryGetValue(name, out var r) ? r.ToArray() : [];
        return h.Length > 0 ? h[^1] : null;
    }

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Sample(); }
            catch (Exception e) { Log.Warn("system sample: " + e.Message); }
            try { await Task.Delay(1000, ct); } catch { break; }
        }
    }

    void Sample()
    {
        var cpu = CpuPercent();
        var (ramUsed, ramTotal) = Memory();
        var ramPct = ramTotal > 0 ? ramUsed * 100.0 / ramTotal : 0;
        var (down, up) = NetRates();
        PingTick();
        var g = gpu.Read();

        var disks = new JsonArray();
        double worstDisk = 0;
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                var used = d.TotalSize - d.TotalFreeSpace;
                var pct = d.TotalSize > 0 ? used * 100.0 / d.TotalSize : 0;
                worstDisk = Math.Max(worstDisk, pct);
                disks.Add(new JsonObject { ["name"] = d.Name.TrimEnd('\\'), ["label"] = d.VolumeLabel, ["used"] = used, ["total"] = d.TotalSize, ["pct"] = Math.Round(pct, 1) });
            }
            catch { /* drive went away */ }
        }

        History["cpu"].Add(cpu);
        History["ram"].Add(ramPct);
        History["down"].Add(down);
        History["up"].Add(up);
        History["disk"].Add(worstDisk);
        if (pingMs is double p) History["ping"].Add(p);
        if (g?.Util is double gu) History["gpu"].Add(gu);
        if (g?.VramPct is double vp) History["vram"].Add(vp);
        if (g?.Temp is double gt) History["gputemp"].Add(gt);

        var s = new JsonObject
        {
            ["cpu"] = Math.Round(cpu, 1),
            ["cores"] = Environment.ProcessorCount,
            ["ram"] = new JsonObject { ["used"] = ramUsed, ["total"] = ramTotal, ["pct"] = Math.Round(ramPct, 1) },
            ["net"] = new JsonObject { ["down"] = Math.Round(down), ["up"] = Math.Round(up) },
            ["ping"] = pingMs is double pm ? Math.Round(pm) : null,
            ["disks"] = disks,
            ["uptime"] = Environment.TickCount64 / 1000,
            ["host"] = Environment.MachineName,
            ["gpu"] = g is null ? null : new JsonObject
            {
                ["name"] = g.Name, ["util"] = g.Util, ["temp"] = g.Temp, ["power"] = g.Power,
                ["vramUsed"] = g.VramUsed, ["vramTotal"] = g.VramTotal, ["vramPct"] = g.VramPct,
            },
        };
        lock (gate) snapshot = s;
    }

    public JsonObject HistoryJson()
    {
        var o = new JsonObject();
        foreach (var (k, r) in History) o[k] = new JsonArray(r.ToArray().Select(v => (JsonNode)v).ToArray());
        return o;
    }

    // ---- CPU via GetSystemTimes ----
    [StructLayout(LayoutKind.Sequential)] struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    double CpuPercent()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
        ulong idle = i.Value, kernel = k.Value, user = u.Value;
        if (lastKernel == 0) { (lastIdle, lastKernel, lastUser) = (idle, kernel, user); return 0; }
        ulong di = idle - lastIdle, dk = kernel - lastKernel, du = user - lastUser;
        (lastIdle, lastKernel, lastUser) = (idle, kernel, user);
        var total = dk + du; // kernel time includes idle time
        return total == 0 ? 0 : Math.Clamp((total - di) * 100.0 / total, 0, 100);
    }

    // ---- memory ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

    static (long used, long total) Memory()
    {
        var m = new MEMORYSTATUSEX();
        if (!GlobalMemoryStatusEx(m)) return (0, 0);
        return ((long)(m.ullTotalPhys - m.ullAvailPhys), (long)m.ullTotalPhys);
    }

    // ---- network (bytes per second, all real adapters) ----
    (double down, double up) NetRates()
    {
        long rx = 0, tx = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var st = n.GetIPStatistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
            }
            catch { /* adapter vanished */ }
        }
        var now = DateTime.UtcNow;
        if (lastRx < 0) { (lastRx, lastTx, lastNetAt) = (rx, tx, now); return (0, 0); }
        var secs = Math.Max(0.2, (now - lastNetAt).TotalSeconds);
        var r = (Math.Max(0, rx - lastRx) / secs, Math.Max(0, tx - lastTx) / secs);
        (lastRx, lastTx, lastNetAt) = (rx, tx, now);
        return r;
    }

    // ---- ping every 5 s, off the sampling thread ----
    int pinging;
    void PingTick()
    {
        if ((DateTime.UtcNow - lastPing).TotalSeconds < 5) return;
        lastPing = DateTime.UtcNow;
        var host = config.Snapshot()["ping"].Str("host");
        if (string.IsNullOrWhiteSpace(host)) { pingMs = null; return; }
        if (Interlocked.Exchange(ref pinging, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var p = new Ping();
                var r = await p.SendPingAsync(host, 2000);
                pingMs = r.Status == IPStatus.Success ? r.RoundtripTime : null;
            }
            catch { pingMs = null; }
            finally { Interlocked.Exchange(ref pinging, 0); }
        });
    }

    public void Dispose() { cts.Cancel(); gpu.Dispose(); }
}

sealed record GpuSample(string? Name, double? Util, double? Temp, double? Power, long? VramUsed, long? VramTotal)
{
    public double? VramPct => VramUsed is long u && VramTotal is long t && t > 0 ? Math.Round(u * 100.0 / t, 1) : null;
}

/// <summary>NVIDIA via nvidia-smi (every 2 s, off-thread); any other GPU via the Windows "GPU Engine" counters.</summary>
sealed class GpuReader : IDisposable
{
    readonly string? smi = FindSmi();
    GpuSample? last;
    DateTime lastAt = DateTime.MinValue;
    int busy;
    // fallback counters
    PerformanceCounterCategory? engine;
    Dictionary<string, PerformanceCounter> counters = new();
    DateTime countersAt = DateTime.MinValue;
    bool fallbackBroken;

    static string? FindSmi()
    {
        foreach (var p in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
            @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
        })
            if (File.Exists(p)) return p;
        return null;
    }

    public GpuSample? Read()
    {
        if ((DateTime.UtcNow - lastAt).TotalSeconds >= 2 && Interlocked.Exchange(ref busy, 1) == 0)
        {
            lastAt = DateTime.UtcNow;
            _ = Task.Run(() =>
            {
                try { last = smi is not null ? ReadSmi() : ReadCounters(); }
                catch { /* keep last */ }
                finally { Interlocked.Exchange(ref busy, 0); }
            });
        }
        return last;
    }

    GpuSample? ReadSmi()
    {
        var psi = new ProcessStartInfo(smi!, "--query-gpu=name,utilization.gpu,temperature.gpu,power.draw,memory.used,memory.total --format=csv,noheader,nounits")
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var line = p.StandardOutput.ReadLine() ?? "";
        p.WaitForExit(3000);
        var f = line.Split(',').Select(x => x.Trim()).ToArray();
        if (f.Length < 6) return null;
        static double? D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return new GpuSample(f[0], D(f[1]), D(f[2]), D(f[3]), D(f[4]) is double u ? (long)(u * 1048576) : null, D(f[5]) is double t ? (long)(t * 1048576) : null);
    }

    GpuSample? ReadCounters()
    {
        if (fallbackBroken) return null;
        try
        {
            engine ??= new PerformanceCounterCategory("GPU Engine");
            if ((DateTime.UtcNow - countersAt).TotalSeconds > 15)
            {
                countersAt = DateTime.UtcNow;
                var names = engine.GetInstanceNames().Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToHashSet();
                foreach (var gone in counters.Keys.Where(k => !names.Contains(k)).ToList()) { counters[gone].Dispose(); counters.Remove(gone); }
                foreach (var n in names.Where(n => !counters.ContainsKey(n)))
                {
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true);
                    c.NextValue();
                    counters[n] = c;
                }
            }
            double sum = 0;
            foreach (var c in counters.Values) { try { sum += c.NextValue(); } catch { } }
            return new GpuSample("GPU", Math.Round(Math.Clamp(sum, 0, 100), 1), null, null, null, null);
        }
        catch (Exception e)
        {
            fallbackBroken = true;
            Log.Warn("GPU counters unavailable: " + e.Message);
            return null;
        }
    }

    public void Dispose() { foreach (var c in counters.Values) c.Dispose(); }
}
