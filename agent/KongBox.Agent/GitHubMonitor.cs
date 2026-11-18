using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>Open pull requests, requested reviews and the latest CI runs of the configured repositories.</summary>
sealed class GitHubMonitor : IDisposable
{
    readonly ConfigStore config;
    readonly HttpClient http;
    readonly CancellationTokenSource cts = new();
    JsonObject state = new() { ["repos"] = new JsonArray() };
    DateTimeOffset next = DateTimeOffset.MinValue;

    public GitHubMonitor(ConfigStore config, HttpClient http)
    {
        (this.config, this.http) = (config, http);
        Task.Run(() => Loop(cts.Token));
    }

    public JsonObject ToJson() => (JsonObject)state.DeepClone();
    public void RefreshNow() => next = DateTimeOffset.MinValue;

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= next)
            {
                next = DateTimeOffset.UtcNow.AddMinutes(2);
                try { await Fetch(ct); }
                catch (Exception e) { state["error"] = e.Message; }
            }
            try { await Task.Delay(3000, ct); } catch { break; }
        }
    }

    async Task<JsonNode?> Get(string url, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (token != "") req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"GitHub {(int)res.StatusCode} em {new Uri(url).AbsolutePath}");
        return Json.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    async Task Fetch(CancellationToken ct)
    {
        var gh = config.Snapshot()["github"];
        var token = gh.Str("token");
        var repos = gh.Arr("repos").Select(r => r?.ToString() ?? "").Where(r => r.Contains('/')).Take(8).ToList();
        if (repos.Count == 0) { state = new JsonObject { ["repos"] = new JsonArray(), ["error"] = null, ["empty"] = true }; return; }
        string? me = null;
        if (token != "") { try { me = (await Get("https://api.github.com/user", token, ct)).Str("login"); } catch { } }
        var list = new JsonArray();
        string? error = null;
        foreach (var repo in repos)
        {
            try
            {
                var pulls = await Get($"https://api.github.com/repos/{repo}/pulls?state=open&per_page=10", token, ct) as JsonArray ?? new JsonArray();
                var runs = (await Get($"https://api.github.com/repos/{repo}/actions/runs?per_page=5", token, ct))?["workflow_runs"] as JsonArray ?? new JsonArray();
                list.Add(new JsonObject
                {
                    ["repo"] = repo,
                    ["pulls"] = new JsonArray(pulls.Select(p => (JsonNode)new JsonObject
                    {
                        ["number"] = p.Num("number"),
                        ["title"] = p.Str("title"),
                        ["author"] = p?["user"].Str("login"),
                        ["draft"] = p.Bool("draft"),
                        ["reviewRequested"] = me != null && p.Arr("requested_reviewers").Any(r => r.Str("login") == me),
                        ["updated"] = p.Str("updated_at"),
                    }).ToArray()),
                    ["runs"] = new JsonArray(runs.Take(3).Select(r => (JsonNode)new JsonObject
                    {
                        ["name"] = r.Str("name"),
                        ["branch"] = r.Str("head_branch"),
                        ["status"] = r.Str("status"),
                        ["conclusion"] = r.Str("conclusion"),
                        ["updated"] = r.Str("updated_at"),
                    }).ToArray()),
                });
            }
            catch (Exception e) { error = e.Message; }
        }
        state = new JsonObject { ["repos"] = list, ["error"] = error, ["me"] = me, ["updated"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
    }

    public void Dispose() => cts.Cancel();
}
