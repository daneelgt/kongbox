using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using QRCoder;

namespace KongBox.Agent;

/// <summary>
/// Wi-Fi access: HTTPS on 8766 with a self-signed certificate the phone pins by its SHA-256 fingerprint,
/// a 6-digit pairing code (5 minutes, 10 tries) and long-lived phone tokens stored only as hashes.
/// </summary>
sealed class Lan
{
    public const int Port = 8766; // the APK download (ApkShare) uses 8767
    static string CertPath => AppPaths.File("lan.pfx");
    static string StorePath => AppPaths.File("pairing.json");

    readonly object gate = new();
    readonly List<JsonObject> phones = new();
    string? code;
    DateTimeOffset codeExpires;
    int codeTries;

    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }

    public Lan()
    {
        Certificate = LoadOrCreateCertificate();
        Fingerprint = Convert.ToHexString(SHA256.HashData(Certificate.RawData)).ToLowerInvariant();
        try
        {
            if (File.Exists(StorePath))
                foreach (var p in Json.Parse(File.ReadAllText(StorePath)).Arr("phones"))
                    if (p is JsonObject o) phones.Add((JsonObject)o.DeepClone());
        }
        catch (Exception e) { Log.Warn("pairing.json: " + e.Message); }
    }

    /// <summary>"AB12 CD34": the first bytes of the fingerprint, shown on both screens during manual pairing.</summary>
    public string CheckCode => (Fingerprint[..4] + " " + Fingerprint[4..8]).ToUpperInvariant();

    static X509Certificate2 LoadOrCreateCertificate()
    {
        try
        {
            if (File.Exists(CertPath))
            {
                var c = X509CertificateLoader.LoadPkcs12FromFile(CertPath, null, X509KeyStorageFlags.Exportable);
                if (c.NotAfter > DateTime.Now.AddDays(30)) return c;
            }
        }
        catch (Exception e) { Log.Warn("lan.pfx unreadable, creating a new one: " + e.Message); }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN=KONGBOX {Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
        File.WriteAllBytes(CertPath, cert.Export(X509ContentType.Pkcs12));
        // re-load so Kestrel gets a certificate whose private key is usable on Windows
        return X509CertificateLoader.LoadPkcs12FromFile(CertPath, null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>IPv4 addresses phones can reach, the one with a default gateway first.</summary>
    public static List<string> Addresses()
    {
        var list = new List<(string ip, int rank)>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
                var desc = (n.Name + " " + n.Description).ToLowerInvariant();
                if (desc.Contains("vethernet") || desc.Contains("hyper-v") || desc.Contains("virtualbox") || desc.Contains("vmware") || desc.Contains("wsl")) continue;
                var props = n.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var a in props.UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = a.Address.ToString();
                    if (ip.StartsWith("169.254.") || ip.StartsWith("127.")) continue;
                    var tailscale = ip.StartsWith("100.") && desc.Contains("tailscale");
                    list.Add((ip, hasGateway ? 0 : tailscale ? 2 : 1));
                }
            }
            catch { /* adapter went away */ }
        }
        return list.OrderBy(x => x.rank).Select(x => x.ip).Distinct().ToList();
    }

    void Save()
    {
        try { File.WriteAllText(StorePath, new JsonObject { ["phones"] = new JsonArray(phones.Select(p => (JsonNode)p.DeepClone()).ToArray()) }.ToJsonString(Json.Pretty)); }
        catch (Exception e) { Log.Warn("pairing save: " + e.Message); }
    }

    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public JsonObject StartPairing()
    {
        lock (gate)
        {
            code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            codeExpires = DateTimeOffset.UtcNow.AddMinutes(5);
            codeTries = 0;
        }
        return PairingJson()!;
    }

    public void CancelPairing() { lock (gate) code = null; }

    JsonObject? PairingJson()
    {
        string? c; DateTimeOffset exp;
        lock (gate) { c = code; exp = codeExpires; }
        if (c is null || exp < DateTimeOffset.UtcNow) return null;
        var ips = Addresses();
        var link = $"kongbox://pair?h={string.Join(",", ips)}&p={Port}&fp={Fingerprint}&c={c}&n={Uri.EscapeDataString(Environment.MachineName)}";
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(link, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(6, "#000000", "#ffffff", true);
        return new JsonObject
        {
            ["code"] = c, ["expires"] = exp.ToUnixTimeMilliseconds(), ["link"] = link, ["qr"] = svg,
        };
    }

    /// <summary>One-time code → a long-lived token for this phone (null when the code is wrong or spent).</summary>
    public (string? token, string? error) Claim(string? attempt, string? name)
    {
        lock (gate)
        {
            if (code is null || codeExpires < DateTimeOffset.UtcNow) return (null, "Nenhum pareamento aberto no PC.");
            if (++codeTries > 10) { code = null; return (null, "Tentativas demais; gere um código novo."); }
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(attempt ?? ""), System.Text.Encoding.ASCII.GetBytes(code)))
                return (null, "Código errado.");
            code = null;
            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            phones.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N")[..10],
                ["name"] = string.IsNullOrWhiteSpace(name) ? "Celular" : name.Trim()[..Math.Min(name.Trim().Length, 40)],
                ["hash"] = Hash(token),
                ["created"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["lastSeen"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            Save();
            return (token, null);
        }
    }

    static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    DateTimeOffset lastTouchSave = DateTimeOffset.MinValue;

    public bool Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100) return false;
        var h = Hash(token);
        lock (gate)
        {
            var p = phones.FirstOrDefault(x => CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(x.Str("hash")), System.Text.Encoding.ASCII.GetBytes(h)));
            if (p is null) return false;
            p["lastSeen"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (DateTimeOffset.UtcNow - lastTouchSave > TimeSpan.FromMinutes(5)) { lastTouchSave = DateTimeOffset.UtcNow; Save(); }
            return true;
        }
    }

    public bool Revoke(string id)
    {
        lock (gate)
        {
            var n = phones.RemoveAll(p => p.Str("id") == id);
            if (n > 0) Save();
            return n > 0;
        }
    }

    public JsonObject Status(bool enabled)
    {
        JsonArray list;
        lock (gate) list = new JsonArray(phones.Select(p => (JsonNode)new JsonObject { ["id"] = p.Str("id"), ["name"] = p.Str("name"), ["created"] = p.Num("created"), ["lastSeen"] = p.Num("lastSeen") }).ToArray());
        return new JsonObject
        {
            ["enabled"] = enabled, ["port"] = Port, ["ips"] = new JsonArray(Addresses().Select(i => (JsonNode)i).ToArray()),
            ["fingerprint"] = Fingerprint, ["check"] = CheckCode, ["phones"] = list, ["pairing"] = PairingJson(),
        };
    }

    /// <summary>Which API paths a paired phone may use over Wi-Fi. The editor and settings stay loopback-only.</summary>
    public static bool PanelPath(string path, string method) =>
        path.StartsWith("/panel/") || path == "/panel"
        || (path == "/api/wallpaper" && method == "GET")
        || path is "/api/config" or "/api/state" or "/api/art" or "/api/proxy" or "/api/procs" or "/api/volume" or "/api/procs/kill" or "/api/rules/pause"
        || path.StartsWith("/api/notify/") || (path == "/api/notify" && method == "DELETE") || path == "/api/clientlog" || path is "/api/github/open" or "/api/github/act" or "/api/github/local" or "/api/sessions/open" or "/api/open" or "/api/refresh"
        || path.StartsWith("/api/run/") || path.StartsWith("/api/media/") || path.StartsWith("/api/approvals/")
        || (path.StartsWith("/api/widget/") && path.EndsWith("/note"));
}
