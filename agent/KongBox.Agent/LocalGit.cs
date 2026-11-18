using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KongBox.Agent;

/// <summary>
/// Finds where GitHub repositories are cloned on this PC (folders next to your AI sessions, Documents,
/// source\repos) and reports/acts on the clone: changes, ahead/behind, pull, push, VS Code, clone.
/// </summary>
sealed class LocalGit
{
    readonly ConfigStore config;
    readonly Func<IEnumerable<string>> extraRoots;
    Dictionary<string, string> clones = new(StringComparer.OrdinalIgnoreCase);
    DateTime scannedAt = DateTime.MinValue;
    readonly object gate = new();

    public LocalGit(ConfigStore config, Func<IEnumerable<string>> extraRoots) => (this.config, this.extraRoots) = (config, extraRoots);

    public List<string> Roots()
    {
        var roots = new List<string>();
        foreach (var r in config.Snapshot()["github"].Arr("roots")) if (r?.ToString() is { Length: > 0 } s) roots.Add(s);
        roots.AddRange(extraRoots());
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        roots.Add(Path.Combine(AppPaths.Home, "source", "repos"));
        roots.Add(Path.Combine(AppPaths.Home, "Documents"));
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static readonly Regex Remote = new(@"github\.com[:/]+([\w.-]+/[\w.-]+?)(?:\.git)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>"owner/name" → local folder, rescanned every 10 minutes (one level deep under each root).</summary>
    public Dictionary<string, string> Clones(bool force = false)
    {
        lock (gate)
        {
            if (!force && (DateTime.UtcNow - scannedAt).TotalMinutes < 10) return clones;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in Roots())
            {
                IEnumerable<string> dirs;
                try { dirs = Directory.EnumerateDirectories(root); } catch { continue; }
                foreach (var d in dirs)
                {
                    var cfg = Path.Combine(d, ".git", "config");
                    if (!File.Exists(cfg)) continue;
                    try
                    {
                        foreach (Match m in Remote.Matches(File.ReadAllText(cfg)))
                            map.TryAdd(m.Groups[1].Value, d);
                    }
                    catch { /* unreadable */ }
                }
            }
            clones = map;
            scannedAt = DateTime.UtcNow;
            return clones;
        }
    }

    public string? PathOf(string repo) => Clones().TryGetValue(repo, out var p) ? p : null;

    static (int code, string output) Git(string args, string? cwd, int timeoutMs = 60_000)
    {
        var psi = new ProcessStartInfo("git", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (cwd != null) psi.WorkingDirectory = cwd;
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0"; // never hang waiting for a password the phone cannot type
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "git demorou demais."); }
        return (p.ExitCode, (o.Result + "\n" + e.Result).Trim());
    }

    // "## feat/v1.2...origin/feat/v1.2 [ahead 1, behind 3]" (branch names may contain dots)
    static readonly Regex Branch = new(@"^## (?<b>.+?)(?:\.\.\.(?<up>\S+))?(?: \[(?<rest>[^\]]*)\])?$", RegexOptions.Compiled);
    static readonly Regex Ahead = new(@"ahead (\d+)", RegexOptions.Compiled);
    static readonly Regex Behind = new(@"behind (\d+)", RegexOptions.Compiled);

    /// <summary>Branch, uncommitted changes, commits to push and to pull (from the last fetch).</summary>
    public JsonObject? Status(string repo)
    {
        var path = PathOf(repo);
        if (path is null) return null;
        var (code, output) = Git("status --porcelain=v1 -b", path, 5000);
        if (code != 0) return new JsonObject { ["path"] = path, ["error"] = "git status falhou" };
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        var m = lines.Length > 0 ? Branch.Match(lines[0]) : Match.Empty;
        var rest = m.Success ? m.Groups["rest"].Value : "";
        var ahead = Ahead.Match(rest);
        var behind = Behind.Match(rest);
        return new JsonObject
        {
            ["path"] = path,
            ["folder"] = Path.GetFileName(path),
            ["branch"] = m.Success ? m.Groups["b"].Value : "",
            ["changes"] = lines.Count(l => !l.StartsWith("##")),
            ["ahead"] = ahead.Success ? int.Parse(ahead.Groups[1].Value) : 0,
            ["behind"] = behind.Success ? int.Parse(behind.Groups[1].Value) : 0,
        };
    }

    /// <summary>Background "git fetch" so ahead/behind stays honest; runs with the status refresh.</summary>
    public void Fetch(string repo)
    {
        var path = PathOf(repo);
        if (path != null) Git("fetch --quiet", path, 30_000);
    }

    public (bool ok, string msg) Act(string repo, string action)
    {
        var path = PathOf(repo);
        switch (action)
        {
            case "clone":
                if (path != null) return (false, "Já está clonado em " + path + ".");
                var root = Roots().FirstOrDefault();
                if (root is null) return (false, "Nenhuma pasta de projetos encontrada.");
                var name = repo.Split('/')[1];
                var target = Path.Combine(root, name);
                if (Directory.Exists(target)) return (false, $"Já existe uma pasta {name} em {root}.");
                var (cc, co) = Git($"clone https://github.com/{repo}.git \"{target}\"", root, 300_000);
                Clones(force: true);
                return cc == 0 ? (true, $"Clonado em {target}.") : (false, Last(co));
        }
        if (path is null) return (false, "Esse repositório não está clonado neste PC.");
        switch (action)
        {
            case "code":
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c code \"{path}\"") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
                return (true, "Abrindo " + Path.GetFileName(path) + " no VS Code.");
            case "folder":
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
                return (true, "Pasta aberta no PC.");
            case "pull":
                var (pc, po) = Git("pull --ff-only", path);
                return pc == 0 ? (true, po.Contains("Already up to date") || po.Contains("Já está atualizado") ? "Já estava atualizado." : "Atualizado (pull).") : (false, Last(po));
            case "push":
                var (sc, so) = Git("push", path);
                return sc == 0 ? (true, "Enviado (push).") : (false, Last(so));
            default:
                return (false, "Ação inválida.");
        }
    }

    static string Last(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(l => l.Trim().Length > 0) ?? output;
        return line.Length > 160 ? line[..160] + "…" : line;
    }
}
