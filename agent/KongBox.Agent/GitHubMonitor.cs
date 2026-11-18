using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>Open pull requests, requested reviews and the latest CI runs of the configured repositories.</summary>
sealed class GitHubMonitor : IDisposable
{
    readonly ConfigStore config;
    readonly HttpClient http;
    public readonly LocalGit Local;
    readonly CancellationTokenSource cts = new();
    JsonObject state = new() { ["repos"] = new JsonArray() };
    DateTimeOffset next = DateTimeOffset.MinValue;

    public GitHubMonitor(ConfigStore config, HttpClient http, LocalGit local)
    {
        (this.config, this.http, Local) = (config, http, local);
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
                var info = await Get($"https://api.github.com/repos/{repo}", token, ct);
                var commits = await Get($"https://api.github.com/repos/{repo}/commits?per_page=5", token, ct) as JsonArray ?? new JsonArray();
                // keep ahead/behind honest for clones on this PC, then read their status
                await Task.Run(() => Local.Fetch(repo), ct);
                var local = Local.Status(repo);
                var pulls = await Get($"https://api.github.com/repos/{repo}/pulls?state=open&per_page=10", token, ct) as JsonArray ?? new JsonArray();
                var runs = (await Get($"https://api.github.com/repos/{repo}/actions/runs?per_page=5", token, ct))?["workflow_runs"] as JsonArray ?? new JsonArray();
                list.Add(new JsonObject
                {
                    ["repo"] = repo,
                    ["private"] = info.Bool("private"),
                    ["language"] = info.Str("language"),
                    ["stars"] = info.Num("stargazers_count"),
                    ["issues"] = info.Num("open_issues_count"),
                    ["pushed"] = info.Str("pushed_at"),
                    ["description"] = info.Str("description"),
                    ["local"] = local,
                    ["commits"] = new JsonArray(commits.Select(c => (JsonNode)new JsonObject
                    {
                        ["sha"] = c.Str("sha").Length >= 7 ? c.Str("sha")[..7] : c.Str("sha"),
                        ["message"] = (c?["commit"].Str("message") ?? "").Split('\n')[0],
                        ["author"] = c?["author"].Str("login") is { Length: > 0 } login ? login : c?["commit"]?["author"].Str("name"),
                        ["date"] = c?["commit"]?["author"].Str("date"),
                    }).ToArray()),
                    ["pulls"] = new JsonArray(pulls.Select(p => (JsonNode)new JsonObject
                    {
                        ["number"] = p.Num("number"),
                        ["title"] = p.Str("title"),
                        ["author"] = p?["user"].Str("login"),
                        ["draft"] = p.Bool("draft"),
                        ["reviewRequested"] = me != null && p.Arr("requested_reviewers").Any(r => r.Str("login") == me),
                        ["updated"] = p.Str("updated_at"),
                        ["branch"] = p?["head"].Str("ref"),
                        ["base"] = p?["base"].Str("ref"),
                        ["sha"] = p?["head"].Str("sha"),
                        ["mine"] = me != null && p?["user"].Str("login") == me,
                    }).ToArray()),
                    ["runs"] = new JsonArray(runs.Take(3).Select(r => (JsonNode)new JsonObject
                    {
                        ["id"] = r.Num("id"),
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
        // check status of each open PR's head commit (success / failure / pending), one request per PR
        foreach (var repo in list.OfType<JsonObject>())
            foreach (var pr in repo.Arr("pulls").OfType<JsonObject>().Take(6))
            {
                try
                {
                    var checks = (await Get($"https://api.github.com/repos/{repo.Str("repo")}/commits/{pr.Str("sha")}/check-runs?per_page=30", token, ct))?["check_runs"] as JsonArray ?? new JsonArray();
                    int ok = 0, bad = 0, run = 0;
                    foreach (var c in checks)
                    {
                        if (c.Str("status") != "completed") run++;
                        else if (c.Str("conclusion") is "success" or "skipped" or "neutral") ok++;
                        else bad++;
                    }
                    pr["checks"] = new JsonObject { ["ok"] = ok, ["failed"] = bad, ["running"] = run };
                }
                catch { /* checks are optional */ }
            }
        state = new JsonObject { ["repos"] = list, ["error"] = error, ["me"] = me, ["updated"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
    }

    /// <summary>Clone, pull, push, VS Code or the folder, for repositories on your list only.</summary>
    public (bool ok, string msg) LocalAct(string repo, string action)
    {
        if (!Configured(repo)) return (false, "Repositório fora da sua lista.");
        var r = Local.Act(repo, action);
        if (r.ok) RefreshNow();
        return r;
    }

    bool Configured(string repo) => config.Snapshot()["github"].Arr("repos").Any(r => string.Equals(r?.ToString(), repo, StringComparison.OrdinalIgnoreCase));

    /// <summary>Web address built here from ids, never taken from the phone.</summary>
    public string? Url(string repo, int? pr, long? run)
    {
        if (!Configured(repo)) return null;
        if (pr is int n) return $"https://github.com/{repo}/pull/{n}";
        if (run is long r) return $"https://github.com/{repo}/actions/runs/{r}";
        return $"https://github.com/{repo}";
    }

    /// <summary>Approve, merge (squash) or re-run failed jobs. Needs a token with write access for that area.</summary>
    public async Task<(bool ok, string msg)> Act(string action, string repo, int? pr, long? run, CancellationToken ct)
    {
        if (!Configured(repo)) return (false, "Repositório fora da sua lista.");
        var token = config.Snapshot()["github"].Str("token");
        if (token == "") return (false, "Sem token do GitHub (Integrações).");
        HttpRequestMessage req;
        switch (action)
        {
            case "approve" when pr is int n:
                req = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repo}/pulls/{n}/reviews")
                { Content = new StringContent("{\"event\":\"APPROVE\"}", System.Text.Encoding.UTF8, "application/json") };
                break;
            case "merge" when pr is int n:
                req = new HttpRequestMessage(HttpMethod.Put, $"https://api.github.com/repos/{repo}/pulls/{n}/merge")
                { Content = new StringContent("{\"merge_method\":\"squash\"}", System.Text.Encoding.UTF8, "application/json") };
                break;
            case "rerun" when run is long r:
                req = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repo}/actions/runs/{r}/rerun-failed-jobs");
                break;
            default: return (false, "Ação inválida.");
        }
        using (req)
        {
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await http.SendAsync(req, ct);
            var body = Json.Parse(await res.Content.ReadAsStringAsync(ct));
            if (res.IsSuccessStatusCode)
            {
                RefreshNow();
                return (true, action switch { "approve" => "PR aprovado.", "merge" => "PR mesclado.", _ => "Jobs que falharam foram re-executados." });
            }
            var ghMsg = body.Str("message");
            if (res.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound && ghMsg.Contains("Resource not accessible", StringComparison.OrdinalIgnoreCase))
                return (false, action == "rerun" ? "Seu token não pode re-executar: dê Read and write em Actions." : "Seu token é só de leitura: dê Read and write em Pull requests.");
            return (false, ghMsg != "" ? ghMsg : $"GitHub respondeu {(int)res.StatusCode}.");
        }
    }

    public void Dispose() => cts.Cancel();
}
