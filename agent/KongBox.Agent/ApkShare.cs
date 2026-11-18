using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KongBox.Agent;

/// <summary>
/// Installs the phone app on any phone without a cable or adb: for 10 minutes the PC serves the APK on
/// http://&lt;ip&gt;:8767/kongbox.apk (QR code in the editor). Only that one file is served, then the port closes.
/// </summary>
sealed class ApkShare
{
    public const int Port = 8767;
    TcpListener? listener;
    CancellationTokenSource? cts;
    public DateTimeOffset Until { get; private set; }
    public bool Open => listener != null && DateTimeOffset.UtcNow < Until;

    /// <summary>The APK embedded in this exe (release builds), or one sitting next to it.</summary>
    public static string? ApkPath()
    {
        var beside = AdbReverse.FindApk();
        if (beside != null) return beside;
        using var s = Resources.Open("KongBox.apk");
        if (s is null) return null;
        var target = AppPaths.File("KongBox.apk");
        if (!File.Exists(target) || new FileInfo(target).Length != s.Length)
        {
            using var o = File.Create(target);
            s.CopyTo(o);
        }
        return target;
    }

    public (bool ok, string msg) Start()
    {
        if (ApkPath() is null) return (false, "Este agente não tem o APK (rode o build de release).");
        Stop();
        try
        {
            listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
        }
        catch (Exception e) { listener = null; return (false, "Porta 8767 ocupada: " + e.Message); }
        Until = DateTimeOffset.UtcNow.AddMinutes(10);
        cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = cts.Token;
        var l = listener;
        _ = Task.Run(async () =>
        {
            try { while (!token.IsCancellationRequested) { var c = await l.AcceptTcpClientAsync(token); _ = Serve(c); } }
            catch { /* stopped or expired */ }
            finally { try { l.Stop(); } catch { } if (listener == l) listener = null; }
        });
        return (true, "ok");
    }

    public void Stop()
    {
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        listener = null;
    }

    static async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var buf = new byte[2048];
                var n = await stream.ReadAsync(buf);
                var first = Encoding.ASCII.GetString(buf, 0, n).Split("\r\n")[0];
                var path = ApkPath();
                if (path != null && (first.StartsWith("GET /kongbox.apk ") || first.StartsWith("GET / ")))
                {
                    var len = new FileInfo(path).Length;
                    var head = "HTTP/1.1 200 OK\r\nContent-Type: application/vnd.android.package-archive\r\n" +
                               $"Content-Length: {len}\r\nContent-Disposition: attachment; filename=\"KongBox.apk\"\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                    await using var f = File.OpenRead(path);
                    await f.CopyToAsync(stream);
                }
                else await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            }
            catch { /* client went away */ }
        }
    }
}
