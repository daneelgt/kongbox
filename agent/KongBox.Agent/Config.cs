using System.Text.Json.Nodes;

namespace KongBox.Agent;

/// <summary>
/// %APPDATA%\KongBox\config.json (JSON with comments). External edits are picked up on the next read;
/// pages written as { "template": "id" } are expanded into real widgets on load.
/// </summary>
sealed class ConfigStore
{
    public static string FilePath => AppPaths.File("config.json");

    readonly object gate = new();
    JsonObject root;
    DateTime stamp;
    long version;

    public static readonly JsonArray Templates = Json.Parse(Resources.Text("templates.json"))!.AsArray();
    public static readonly JsonArray Catalog = Json.Parse(Resources.Text("widgets.json"))!.AsArray();

    public ConfigStore()
    {
        if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Resources.Text("config.default.json"));
        root = Load() ?? Default();
        if (Expand(root)) Persist(root);
    }

    public long Version { get { lock (gate) { Refresh(); return version; } } }

    static JsonObject Default() => Json.Parse(Resources.Text("config.default.json"))!.AsObject();

    JsonObject? Load()
    {
        try
        {
            stamp = File.GetLastWriteTimeUtc(FilePath);
            var n = Json.Parse(File.ReadAllText(FilePath)) as JsonObject;
            version++;
            return n;
        }
        catch (Exception e)
        {
            Log.Warn("config.json could not be read: " + e.Message);
            return null;
        }
    }

    void Refresh()
    {
        try
        {
            if (File.GetLastWriteTimeUtc(FilePath) == stamp) return;
            var n = Load();
            if (n is null) return;
            root = n;
            if (Expand(root)) Persist(root);
        }
        catch { /* keep the last good config */ }
    }

    /// <summary>A private copy of the whole config (commands and secrets included: never send to the phone).</summary>
    public JsonObject Snapshot()
    {
        lock (gate) { Refresh(); return (JsonObject)root.DeepClone(); }
    }

    public void Save(JsonObject next)
    {
        Validate(next);
        lock (gate)
        {
            Expand(next);
            Persist(next);
            root = next;
            version++;
        }
    }

    void Persist(JsonObject cfg)
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, cfg.ToJsonString(Json.Pretty));
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
        File.Move(tmp, FilePath, true);
        stamp = File.GetLastWriteTimeUtc(FilePath);
    }

    static readonly HashSet<string> KnownTypes = Catalog.Select(w => w.Str("type")).ToHashSet();

    /// <summary>Rejects layouts the panel could not draw. Throws ArgumentException with a readable message.</summary>
    public static void Validate(JsonObject cfg)
    {
        if (cfg["pages"] is not JsonArray pages) throw new ArgumentException("\"pages\" precisa ser uma lista.");
        var ids = new HashSet<string>();
        foreach (var p in pages)
        {
            if (p is not JsonObject po) throw new ArgumentException("Cada página precisa ser um objeto.");
            if (po["template"] is not null) continue;
            var id = po.Str("id");
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Toda página precisa de um id.");
            if (!ids.Add(id)) throw new ArgumentException($"Id de página repetido: {id}");
            foreach (var w in po.Arr("widgets")) ValidateWidget(w, id);
        }
        var sc = new HashSet<string>();
        foreach (var s in cfg.Arr("shortcuts"))
        {
            var id = s.Str("id");
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Todo atalho precisa de um id.");
            if (!sc.Add(id)) throw new ArgumentException($"Id de atalho repetido: {id}");
        }
    }

    static void ValidateWidget(JsonNode? w, string page)
    {
        var type = w.Str("type");
        if (!KnownTypes.Contains(type)) throw new ArgumentException($"Widget desconhecido \"{type}\" na página {page}.");
        double x = w.Num("x"), y = w.Num("y"), ww = w.Num("w", 1), hh = w.Num("h", 1);
        if (x < 0 || y < 0 || ww < 1 || hh < 1 || x + ww > 12 || y + hh > 6)
            throw new ArgumentException($"Widget \"{type}\" fora da grade 12 × 6 na página {page}.");
        foreach (var child in w?["options"]?["cards"] as JsonArray ?? new JsonArray())
            if (!KnownTypes.Contains(child.Str("type")) || child.Str("type") == "stack")
                throw new ArgumentException($"Card inválido dentro do Smart Stack na página {page}.");
    }

    /// <summary>Replaces template placeholders with widgets and gives every page/widget a stable id.</summary>
    static bool Expand(JsonObject cfg)
    {
        bool changed = false;
        if (cfg["pages"] is not JsonArray pages) { cfg["pages"] = new JsonArray(); return true; }
        var used = new HashSet<string>();
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i] is not JsonObject p) continue;
            var tid = p.Str("template");
            if (tid != "")
            {
                var t = Templates.FirstOrDefault(x => x.Str("id") == tid);
                if (t is null) { pages.RemoveAt(i--); changed = true; continue; }
                var np = new JsonObject
                {
                    ["id"] = tid,
                    ["title"] = t.Str("title"),
                    ["icon"] = t.Str("icon"),
                    ["enabled"] = p.Bool("enabled", true),
                    ["widgets"] = t["widgets"]!.DeepClone(),
                };
                pages[i] = np;
                p = np;
                changed = true;
            }
            var id = p.Str("id");
            if (id == "" || used.Contains(id))
            {
                var baseId = id == "" ? "page" : id;
                int n = 2;
                id = baseId;
                while (used.Contains(id)) id = $"{baseId}-{n++}";
                p["id"] = id;
                changed = true;
            }
            used.Add(id);
            int k = 1;
            var wids = new HashSet<string>();
            foreach (var w in p.Arr("widgets"))
            {
                if (w is not JsonObject wo) continue;
                var wid = wo.Str("id");
                if (wid == "" || !wids.Add(wid))
                {
                    do wid = $"w{k++}"; while (wids.Contains(wid));
                    wids.Add(wid);
                    wo["id"] = wid;
                    changed = true;
                }
            }
        }
        return changed;
    }

    /// <summary>What the phone may see: layout, display options and shortcut faces. Never commands or keys.</summary>
    public JsonObject PanelConfig()
    {
        var c = Snapshot();
        var pages = new JsonArray();
        foreach (var p in c.Arr("pages"))
            if (p.Bool("enabled", true)) pages.Add(p!.DeepClone());
        var shortcuts = new JsonArray();
        foreach (var s in c.Arr("shortcuts"))
            shortcuts.Add(new JsonObject
            {
                ["id"] = s.Str("id"),
                ["label"] = s.Str("label"),
                ["icon"] = s.Str("icon"),
                ["color"] = s.Str("color"),
                ["toggle"] = s.Str("toggle"),
            });
        return new JsonObject
        {
            ["version"] = Version,
            ["pages"] = pages,
            ["shortcuts"] = shortcuts,
            ["display"] = c["display"]?.DeepClone() ?? new JsonObject(),
            ["night"] = c["night"]?.DeepClone() ?? new JsonObject(),
            ["appearance"] = c["appearance"]?.DeepClone() ?? new JsonObject(),
            ["weather"] = c["weather"]?.DeepClone() ?? new JsonObject(),
            ["worldclocks"] = c["worldclocks"]?.DeepClone() ?? new JsonArray(),
        };
    }
}
