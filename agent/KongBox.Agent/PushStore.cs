using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>Webhook values (POST /api/push/key), notifications (POST /api/notify) and page-switch requests.</summary>
sealed class PushStore
{
    static string FilePath => AppPaths.File("push.json");
    readonly object gate = new();
    readonly Dictionary<string, JsonObject> values = new();
    readonly List<JsonObject> notices = new();
    long nextNotice = 1;
    JsonObject? pendingGoto;
    long gotoSeq;

    public PushStore()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var root = Json.Parse(File.ReadAllText(FilePath));
            foreach (var (k, v) in root?["values"] as JsonObject ?? new JsonObject())
                if (v is JsonObject o) values[k] = (JsonObject)o.DeepClone();
            foreach (var n in root.Arr("notices"))
                if (n is JsonObject o) notices.Add((JsonObject)o.DeepClone());
            // the counter is saved so ids never repeat, even after the inbox is cleared and the agent restarts
            nextNotice = Math.Max((long)root.Num("nextId", 1), notices.Count == 0 ? 1 : (long)notices.Max(n => n.Num("id")) + 1);
        }
        catch (Exception e) { Log.Warn("push.json: " + e.Message); }
    }

    void Save()
    {
        try
        {
            var o = new JsonObject
            {
                ["values"] = new JsonObject(values.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value.DeepClone()))),
                ["notices"] = new JsonArray(notices.Select(n => (JsonNode)n.DeepClone()).ToArray()),
                ["nextId"] = nextNotice,
            };
            File.WriteAllText(FilePath, o.ToJsonString(Json.Pretty));
        }
        catch (Exception e) { Log.Warn("push save: " + e.Message); }
    }

    /// <summary>Plain text, a number, or JSON { value, title, sub, unit, icon, color, level, progress, items, ttl }.</summary>
    public JsonObject Set(string key, string body, int? ttl)
    {
        JsonObject o;
        var trimmed = body.Trim();
        JsonNode? parsed = null;
        if (trimmed.StartsWith('{')) { try { parsed = Json.Parse(trimmed); } catch { } }
        if (parsed is JsonObject po) o = po;
        else o = new JsonObject { ["value"] = trimmed };
        var t = ttl ?? (o["ttl"] is not null ? (int?)o.Num("ttl") : null);
        o.Remove("ttl");
        o["updated"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (t is int s && s > 0) o["expires"] = DateTimeOffset.UtcNow.AddSeconds(s).ToUnixTimeMilliseconds();
        lock (gate) { values[key] = o; Save(); }
        return o;
    }

    public bool Remove(string key) { lock (gate) { var r = values.Remove(key); Save(); return r; } }
    public void Clear() { lock (gate) { values.Clear(); Save(); } }

    public string? Value(string key)
    {
        lock (gate) { Expire(); return values.TryGetValue(key, out var o) ? o.Str("value", o["value"]?.ToString() ?? "") : null; }
    }

    void Expire()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gone = values.Where(kv => kv.Value["expires"] is not null && kv.Value.Num("expires") < now).Select(kv => kv.Key).ToList();
        foreach (var k in gone) values.Remove(k);
        if (gone.Count > 0) Save();
    }

    public JsonObject Values()
    {
        lock (gate)
        {
            Expire();
            return new JsonObject(values.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value.DeepClone())));
        }
    }

    /// <summary>{ title, text, level, vibrate, seconds, wake, page }.</summary>
    public JsonObject Notify(JsonObject n)
    {
        lock (gate)
        {
            n["id"] = nextNotice++;
            n["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            notices.Add(n);
            while (notices.Count > 50) notices.RemoveAt(0);
            Save();
        }
        if (n.Str("page") is { Length: > 0 } page) Goto(page, 3);
        return n;
    }

    public JsonArray Notices(long since = 0, int limit = 50)
    {
        lock (gate) return new JsonArray(notices.Where(n => n.Num("id") > since).TakeLast(limit).Select(n => (JsonNode)n.DeepClone()).ToArray());
    }

    public long LastNoticeId { get { lock (gate) return nextNotice - 1; } }

    public JsonObject? Notice(long id) { lock (gate) return notices.FirstOrDefault(n => (long)n.Num("id") == id)?.DeepClone() as JsonObject; }
    public bool RemoveNotice(long id) { lock (gate) { var n = notices.RemoveAll(x => (long)x.Num("id") == id); if (n > 0) Save(); return n > 0; } }

    /// <summary>Empties the notification inbox (ids keep counting so phones do not replay old ones).</summary>
    public void ClearNotices() { lock (gate) { notices.Clear(); Save(); } }

    public void Goto(string page, int countdown)
    {
        lock (gate) pendingGoto = new JsonObject { ["seq"] = ++gotoSeq, ["page"] = page, ["countdown"] = Math.Clamp(countdown, 0, 10), ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
    }

    /// <summary>The latest page request (stale after 30 s so a reconnecting phone does not jump).</summary>
    public JsonObject? PendingGoto()
    {
        lock (gate)
        {
            if (pendingGoto is null) return null;
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - pendingGoto.Num("at") > 30_000) return null;
            return (JsonObject)pendingGoto.DeepClone();
        }
    }
}
