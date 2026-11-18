using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>"When X holds (for N seconds), do Y" — checked every 2 s, with cooldowns and a global pause.</summary>
sealed class RuleEngine : IDisposable
{
    readonly ConfigStore config;
    readonly SystemMonitor sys;
    readonly ProcessMonitor procs;
    readonly PushStore push;
    readonly Shortcuts shortcuts;
    readonly CancellationTokenSource cts = new();
    readonly Dictionary<string, DateTime> holdingSince = new();
    readonly Dictionary<string, DateTime> lastFired = new();
    readonly Dictionary<string, bool> wasTrue = new();
    readonly object gate = new();
    public DateTime PausedUntil { get; private set; } = DateTime.MinValue;

    public RuleEngine(ConfigStore config, SystemMonitor sys, ProcessMonitor procs, PushStore push, Shortcuts shortcuts)
    {
        (this.config, this.sys, this.procs, this.push, this.shortcuts) = (config, sys, procs, push, shortcuts);
        Task.Run(() => Loop(cts.Token));
    }

    public void Pause(int minutes) => PausedUntil = minutes <= 0 ? DateTime.MinValue : DateTime.UtcNow.AddMinutes(minutes);

    async Task Loop(CancellationToken ct)
    {
        await Task.Delay(5000, ct).ContinueWith(_ => { });
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); } catch (Exception e) { Log.Warn("rules: " + e.Message); }
            try { await Task.Delay(2000, ct); } catch { break; }
        }
    }

    bool? Holds(JsonNode? when)
    {
        if (when is null) return null;
        if (when.Str("metric") is { Length: > 0 } m)
        {
            if (sys.Metric(m) is not double v) return null;
            var target = when.Num("value");
            return when.Str("op", ">") switch
            {
                ">" => v > target, ">=" => v >= target, "<" => v < target, "<=" => v <= target, "==" => Math.Abs(v - target) < 0.001,
                _ => null,
            };
        }
        if (when.Str("process") is { Length: > 0 } p) return procs.IsRunning(p) == when.Bool("running", true);
        if (when.Str("push") is { Length: > 0 } key)
        {
            var val = push.Value(key);
            return when["equals"] is null ? val is not null : string.Equals(val, when.Str("equals"), StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }

    void Tick()
    {
        if (DateTime.UtcNow < PausedUntil) return;
        var now = DateTime.UtcNow;
        foreach (var r in config.Snapshot().Arr("rules"))
        {
            var id = r.Str("id");
            if (id == "" || !r.Bool("enabled", true)) continue;
            var holds = Holds(r?["when"]) == true;
            lock (gate)
            {
                if (!holds) { holdingSince.Remove(id); wasTrue[id] = false; continue; }
                if (!holdingSince.ContainsKey(id)) holdingSince[id] = now;
                var held = (now - holdingSince[id]).TotalSeconds;
                if (held < r?["when"].Num("forSec", 0)) continue;
                // fire on the rising edge, again only after the cooldown
                var cooldown = r.Num("cooldownSec", 300);
                if (wasTrue.GetValueOrDefault(id) && lastFired.TryGetValue(id, out var lf) && (now - lf).TotalSeconds < cooldown) continue;
                if (lastFired.TryGetValue(id, out lf) && (now - lf).TotalSeconds < cooldown) continue;
                wasTrue[id] = true;
                lastFired[id] = now;
            }
            _ = Fire(r);
        }
    }

    public async Task Fire(JsonNode? rule)
    {
        foreach (var a in rule.Arr("then"))
        {
            try
            {
                if (a?["notify"] is JsonObject n) push.Notify((JsonObject)n.DeepClone());
                else if (a.Str("goto") is { Length: > 0 } g) push.Goto(g, 3);
                else if (a.Str("run") is { Length: > 0 } run) await shortcuts.Run(run);
                else if (a?["push"] is JsonObject p) push.Set(p.Str("key"), p["value"]?.ToJsonString().Trim('"') ?? "", null);
            }
            catch (Exception e) { Log.Warn($"rule {rule.Str("id")}: {e.Message}"); }
        }
    }

    public JsonArray Status()
    {
        var now = DateTime.UtcNow;
        var arr = new JsonArray();
        foreach (var r in config.Snapshot().Arr("rules"))
        {
            var id = r.Str("id");
            bool holding;
            DateTime? fired;
            lock (gate) { holding = holdingSince.ContainsKey(id); fired = lastFired.TryGetValue(id, out var f) ? f : null; }
            arr.Add(new JsonObject
            {
                ["id"] = id, ["name"] = r.Str("name", id), ["enabled"] = r.Bool("enabled", true), ["holding"] = holding,
                ["lastFired"] = fired is DateTime d ? new DateTimeOffset(d).ToUnixTimeMilliseconds() : null,
            });
        }
        return arr;
    }

    public void Dispose() => cts.Cancel();
}
