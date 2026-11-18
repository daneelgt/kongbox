using System.Collections.Concurrent;

namespace KongBox.Agent;

/// <summary>
/// The panel's public data feeds (weather, air, rates, crypto, Hacker News) go through the agent: one cache for
/// every phone, no CORS or offline errors in the WebView, and only these hosts are reachable.
/// </summary>
sealed class FeedProxy(HttpClient http)
{
    static readonly Dictionary<string, TimeSpan> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["api.open-meteo.com"] = TimeSpan.FromMinutes(10),
        ["air-quality-api.open-meteo.com"] = TimeSpan.FromMinutes(20),
        ["geocoding-api.open-meteo.com"] = TimeSpan.FromHours(12),
        ["api.frankfurter.dev"] = TimeSpan.FromMinutes(30),
        ["api.frankfurter.app"] = TimeSpan.FromMinutes(30),
        ["api.coingecko.com"] = TimeSpan.FromMinutes(2),
        ["hacker-news.firebaseio.com"] = TimeSpan.FromMinutes(5),
    };

    readonly ConcurrentDictionary<string, (DateTimeOffset at, int status, string type, byte[] body)> cache = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();

    public static bool Allowed(string url, out Uri uri) =>
        Uri.TryCreate(url, UriKind.Absolute, out uri!) && uri.Scheme == "https" && Hosts.ContainsKey(uri.Host);

    public async Task<(int status, string type, byte[] body)> Get(Uri uri, CancellationToken ct)
    {
        var key = uri.AbsoluteUri;
        var ttl = Hosts[uri.Host];
        if (cache.TryGetValue(key, out var c) && DateTimeOffset.UtcNow - c.at < ttl) return (c.status, c.type, c.body);
        var gate = locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out c) && DateTimeOffset.UtcNow - c.at < ttl) return (c.status, c.type, c.body);
            try
            {
                using var res = await http.GetAsync(uri, ct);
                var body = await res.Content.ReadAsByteArrayAsync(ct);
                var type = res.Content.Headers.ContentType?.ToString() ?? "application/json";
                if (res.IsSuccessStatusCode)
                {
                    cache[key] = (DateTimeOffset.UtcNow, 200, type, body);
                    if (cache.Count > 300) foreach (var old in cache.OrderBy(x => x.Value.at).Take(100)) cache.TryRemove(old.Key, out _);
                }
                else if (cache.TryGetValue(key, out c)) return (c.status, c.type, c.body); // stale beats an error
                return ((int)res.StatusCode, type, body);
            }
            catch when (cache.TryGetValue(key, out c)) { return (c.status, c.type, c.body); }
        }
        finally { gate.Release(); }
    }
}
