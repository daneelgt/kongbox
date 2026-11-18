using System.Diagnostics;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>Processes grouped by name, CPU share from TotalProcessorTime deltas, sampled every 2 s.</summary>
sealed class ProcessMonitor : IDisposable
{
    readonly CancellationTokenSource cts = new();
    readonly object gate = new();
    Dictionary<int, TimeSpan> lastCpu = new();
    DateTime lastAt = DateTime.UtcNow;
    List<ProcGroup> groups = new();

    // never offered for "end task": ending these crashes or logs out Windows
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "fontdrvhost", "dwm", "Memory Compression", "Secure System", "LsaIso", "KongBoxAgent", "MsMpEng", "audiodg",
    };

    public sealed record ProcGroup(string Name, double Cpu, long Mem, int Count, int[] Pids, bool Protected);

    public ProcessMonitor() => Task.Run(() => Loop(cts.Token));

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Sample(); } catch (Exception e) { Log.Warn("procs: " + e.Message); }
            try { await Task.Delay(2000, ct); } catch { break; }
        }
    }

    void Sample()
    {
        var now = DateTime.UtcNow;
        var secs = Math.Max(0.5, (now - lastAt).TotalSeconds);
        var next = new Dictionary<int, TimeSpan>();
        var acc = new Dictionary<string, (double cpu, long mem, List<int> pids)>(StringComparer.OrdinalIgnoreCase);
        var cores = Environment.ProcessorCount;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == 0) continue;
                    var name = p.ProcessName;
                    long mem = 0;
                    try { mem = p.WorkingSet64; } catch { }
                    double cpu = 0;
                    try
                    {
                        var t = p.TotalProcessorTime;
                        next[p.Id] = t;
                        if (lastCpu.TryGetValue(p.Id, out var prev)) cpu = (t - prev).TotalSeconds / secs / cores * 100;
                    }
                    catch { /* access denied for protected processes */ }
                    if (!acc.TryGetValue(name, out var a)) a = (0, 0, new List<int>());
                    a.cpu += cpu; a.mem += mem; a.pids.Add(p.Id);
                    acc[name] = a;
                }
                catch { /* process exited */ }
            }
        }
        lastCpu = next;
        lastAt = now;
        var list = acc.Select(kv => new ProcGroup(kv.Key, Math.Round(Math.Clamp(kv.Value.cpu, 0, 100), 1), kv.Value.mem, kv.Value.pids.Count, kv.Value.pids.ToArray(), Protected.Contains(kv.Key)))
                      .OrderByDescending(g => g.Cpu).ThenByDescending(g => g.Mem).ToList();
        lock (gate) groups = list;
    }

    public List<ProcGroup> Groups() { lock (gate) return groups; }

    public bool IsRunning(string name)
    {
        name = Path.GetFileNameWithoutExtension(name);
        lock (gate) return groups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public JsonArray ToJson(int limit, string sort = "cpu")
    {
        var g = Groups();
        IEnumerable<ProcGroup> q = sort == "mem" ? g.OrderByDescending(x => x.Mem) : g;
        return new JsonArray(q.Where(x => x.Name != "Idle").Take(limit).Select(x => (JsonNode)new JsonObject
        {
            ["name"] = x.Name, ["cpu"] = x.Cpu, ["mem"] = x.Mem, ["count"] = x.Count, ["protected"] = x.Protected,
        }).ToArray());
    }

    /// <summary>Ends one instance (the heaviest) or every instance of a process name.</summary>
    public (bool ok, string msg) Kill(string name, bool all)
    {
        if (Protected.Contains(name)) return (false, "Processo protegido pelo sistema.");
        var targets = Process.GetProcessesByName(name);
        if (targets.Length == 0) return (false, "Processo não encontrado.");
        try
        {
            var chosen = all ? targets : targets.OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } }).Take(1).ToArray();
            int n = 0;
            foreach (var p in chosen) { try { p.Kill(true); n++; } catch { } }
            return n > 0 ? (true, $"{n} encerrado(s).") : (false, "Sem permissão para encerrar.");
        }
        finally { foreach (var p in targets) p.Dispose(); }
    }

    public void Dispose() => cts.Cancel();
}
