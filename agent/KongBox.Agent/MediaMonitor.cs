using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Windows.Media.Control;

namespace KongBox.Agent;

/// <summary>Now playing from the Windows media session (Spotify, browsers, any SMTC app) with artwork.</summary>
sealed class MediaMonitor : IDisposable
{
    readonly CancellationTokenSource cts = new();
    readonly object gate = new();
    GlobalSystemMediaTransportControlsSessionManager? manager;
    JsonObject? state;
    byte[]? art;
    string artType = "image/jpeg";
    string artKey = "";

    public MediaMonitor() => Task.Run(() => Loop(cts.Token));

    public JsonObject? State() { lock (gate) return state?.DeepClone() as JsonObject; }
    public (byte[]? data, string type) Art() { lock (gate) return (art, artType); }

    async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                await Sample();
            }
            catch (Exception e) { Log.Warn("media: " + e.Message); lock (gate) state = null; }
            try { await Task.Delay(1000, ct); } catch { break; }
        }
    }

    async Task Sample()
    {
        var s = manager?.GetCurrentSession();
        if (s is null) { lock (gate) state = null; return; }
        var props = await s.TryGetMediaPropertiesAsync();
        var info = s.GetPlaybackInfo();
        var tl = s.GetTimelineProperties();
        var playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        // position extrapolated from the last update while playing
        var pos = tl.Position;
        if (playing && tl.LastUpdatedTime.Year > 2000) pos += DateTimeOffset.Now - tl.LastUpdatedTime;
        var dur = tl.EndTime - tl.StartTime;
        if (dur > TimeSpan.Zero && pos > dur) pos = dur;

        var key = $"{props.Title}|{props.Artist}|{props.AlbumTitle}";
        if (key != artKey)
        {
            byte[]? bytes = null;
            if (props.Thumbnail is not null)
            {
                try
                {
                    using var ras = await props.Thumbnail.OpenReadAsync();
                    using var st = ras.AsStreamForRead();
                    using var ms = new MemoryStream();
                    await st.CopyToAsync(ms);
                    bytes = ms.ToArray();
                    lock (gate) artType = string.IsNullOrEmpty(ras.ContentType) ? "image/jpeg" : ras.ContentType;
                }
                catch { /* some players expose no artwork */ }
            }
            lock (gate) { art = bytes; artKey = key; }
        }

        string? artId;
        lock (gate) artId = art is null ? null : Convert.ToHexString(SHA1.HashData(art))[..12];
        var app = s.SourceAppUserModelId ?? "";
        var o = new JsonObject
        {
            ["title"] = props.Title,
            ["artist"] = props.Artist,
            ["album"] = props.AlbumTitle,
            ["app"] = FriendlyApp(app),
            ["playing"] = playing,
            ["pos"] = Math.Round(pos.TotalSeconds, 1),
            ["dur"] = Math.Round(Math.Max(0, dur.TotalSeconds), 1),
            ["art"] = artId,
            ["canSeek"] = info.Controls.IsPlaybackPositionEnabled,
        };
        lock (gate) state = o;
    }

    static string FriendlyApp(string aumid)
    {
        var a = aumid.ToLowerInvariant();
        if (a.Contains("spotify")) return "Spotify";
        if (a.Contains("chrome")) return "Chrome";
        if (a.Contains("msedge")) return "Edge";
        if (a.Contains("firefox")) return "Firefox";
        if (a.Contains("zune") || a.Contains("media")) return "Mídia";
        // Firefox and some Chromium builds register a bare 16-hex install hash
        if (aumid.Length == 16 && aumid.All(Uri.IsHexDigit)) return "Navegador";
        var name = aumid.Split('!', '\\', '_').FirstOrDefault(x => x.Length > 0) ?? aumid;
        return name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> Action(string action, double? seconds = null)
    {
        var s = manager?.GetCurrentSession();
        if (s is null) return KeySender.MediaKey(action);
        return action switch
        {
            "play-pause" or "toggle" => await s.TryTogglePlayPauseAsync(),
            "next" => await s.TrySkipNextAsync(),
            "prev" => await s.TrySkipPreviousAsync(),
            "seek" when seconds is double sec => await s.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(sec).Ticks),
            _ => false,
        };
    }

    public void Dispose() => cts.Cancel();
}
