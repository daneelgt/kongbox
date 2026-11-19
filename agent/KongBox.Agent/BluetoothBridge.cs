using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace KongBox.Agent;

/// <summary>
/// Bluetooth link for the phone: an RFCOMM service (Windows Bluetooth stack) that carries the panel's HTTP requests
/// as length-prefixed JSON frames and answers them from the local agent. Only devices paired in Windows Bluetooth
/// settings can connect (encrypted, authenticated link), and only the panel's API is reachable, like over Wi-Fi.
/// Frame: 4-byte big-endian length + UTF-8 JSON. Request {i, m, p, h, b?}; response {i, s, t, b} (b = base64 body).
/// </summary>
sealed class BluetoothBridge : IDisposable
{
    public static readonly Guid ServiceUuid = Guid.Parse("6c1f2a8e-4b1d-4f6a-9a2e-6b6f6e67626f");
    readonly ConfigStore config;
    readonly HttpClient local = new() { BaseAddress = new Uri("http://127.0.0.1:8765"), Timeout = TimeSpan.FromSeconds(20) };
    RfcommServiceProvider? provider;
    StreamSocketListener? listener;
    int clients;
    public string? Error { get; private set; }
    public bool Advertising { get; private set; }
    public int Clients => clients;
    public DateTimeOffset LastRequest { get; private set; }

    public BluetoothBridge(ConfigStore config)
    {
        this.config = config;
        _ = Start();
    }

    async Task Start()
    {
        if (!config.Snapshot()["bluetooth"].Bool("enabled", true)) { Error = null; return; }
        try
        {
            provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(ServiceUuid));
            listener = new StreamSocketListener();
            listener.ConnectionReceived += (sender, a) => { _ = Serve(a.Socket); };
            await listener.BindServiceNameAsync(provider.ServiceId.AsString(), SocketProtectionLevel.BluetoothEncryptionWithAuthentication);
            // SDP service name (attribute 0x100) so the service shows up as "KONGBOX"
            var w = new DataWriter();
            var name = Encoding.UTF8.GetBytes("KONGBOX");
            w.WriteByte(0x25);              // text string, 1-byte length
            w.WriteByte((byte)name.Length);
            w.WriteBytes(name);
            provider.SdpRawAttributes.Add(0x100, w.DetachBuffer());
            provider.StartAdvertising(listener, true);
            Advertising = true;
            Error = null;
            Log.Info("bluetooth: RFCOMM service advertised");
        }
        catch (Exception e)
        {
            Advertising = false;
            Error = "Bluetooth indisponível neste PC (" + e.Message.Split('\n')[0].Trim() + ")";
            Log.Warn("bluetooth: " + e.Message);
        }
    }

    async Task Serve(StreamSocket socket)
    {
        Interlocked.Increment(ref clients);
        var gate = new SemaphoreSlim(1, 1);
        try
        {
            using (socket)
            using (var input = socket.InputStream.AsStreamForRead())
            using (var output = socket.OutputStream.AsStreamForWrite())
            {
                var head = new byte[4];
                while (true)
                {
                    if (!await ReadExactly(input, head)) break;
                    var len = BinaryPrimitives.ReadInt32BigEndian(head);
                    if (len <= 0 || len > 4_000_000) break;
                    var buf = new byte[len];
                    if (!await ReadExactly(input, buf)) break;
                    var req = JsonNode.Parse(buf) as JsonObject;
                    if (req is null) continue;
                    // requests run concurrently; responses carry the request id
                    _ = Task.Run(async () =>
                    {
                        var res = await Handle(req);
                        var bytes = Encoding.UTF8.GetBytes(res.ToJsonString());
                        var frame = new byte[4 + bytes.Length];
                        BinaryPrimitives.WriteInt32BigEndian(frame, bytes.Length);
                        bytes.CopyTo(frame, 4);
                        await gate.WaitAsync();
                        try { await output.WriteAsync(frame); await output.FlushAsync(); }
                        catch { /* link dropped */ }
                        finally { gate.Release(); }
                    });
                }
            }
        }
        catch { /* link dropped */ }
        finally { Interlocked.Decrement(ref clients); }
    }

    static async Task<bool> ReadExactly(Stream s, byte[] buf)
    {
        int read = 0;
        while (read < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(read));
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    async Task<JsonObject> Handle(JsonObject req)
    {
        var id = req["i"]?.DeepClone();
        var method = req.Str("m", "GET").ToUpperInvariant();
        var pathAndQuery = req.Str("p", "/");
        var path = pathAndQuery.Split('?')[0];
        LastRequest = DateTimeOffset.UtcNow;
        JsonObject Reply(int status, string type, byte[] body) => new() { ["i"] = id, ["s"] = status, ["t"] = type, ["b"] = Convert.ToBase64String(body) };

        // same surface as the Wi-Fi port: the panel and its API, never the editor or the settings
        if (!(path == "/api/hello" || Lan.PanelPath(path, method)) || pathAndQuery.Contains("..")) return Reply(404, "text/plain", []);
        try
        {
            using var msg = new HttpRequestMessage(new HttpMethod(method), pathAndQuery);
            if (req.Str("b") is { Length: > 0 } b64)
                msg.Content = new ByteArrayContent(Convert.FromBase64String(b64));
            var headers = req["h"] as JsonObject;
            var ctype = headers.Str("Content-Type");
            if (msg.Content != null && ctype != "") msg.Content.Headers.TryAddWithoutValidation("Content-Type", ctype);
            if (headers.Str("X-Kong") == "1") msg.Headers.Add("X-Kong", "1");
            using var res = await local.SendAsync(msg);
            var body = await res.Content.ReadAsByteArrayAsync();
            return Reply((int)res.StatusCode, res.Content.Headers.ContentType?.ToString() ?? "application/octet-stream", body);
        }
        catch (Exception e) { return Reply(502, "text/plain", Encoding.UTF8.GetBytes(e.Message)); }
    }

    public JsonObject Status() => new()
    {
        ["enabled"] = config.Snapshot()["bluetooth"].Bool("enabled", true),
        ["advertising"] = Advertising,
        ["clients"] = clients,
        ["error"] = Error,
        ["lastRequest"] = LastRequest == default ? null : LastRequest.ToUnixTimeMilliseconds(),
    };

    public void Dispose()
    {
        try { provider?.StopAdvertising(); } catch { }
        try { listener?.Dispose(); } catch { }
    }
}
