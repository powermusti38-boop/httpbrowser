using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamBrowser.Core;

public sealed class VlcLauncher : IDisposable
{
    public bool DiagnosticMode { get; set; }
    public double LastConfirmedTime { get; private set; }
    public long LastDecodedVideo { get; private set; }
    public long LastDecodedAudio { get; private set; }
    public string LastStatusJson { get; private set; } = "";
    private readonly List<(Process Process, HttpClient Client)> sessions = [];
    public static string? FindInstalled(string? configured)
    {
        foreach (var path in new[] { configured,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe") })
            if (path is not null && File.Exists(path)) return path;
        return null;
    }
    public async Task<(bool Success, string Message)> PlayAsync(string executable, StreamSource source, double seconds, bool live, CancellationToken ct)
    {
        if (!File.Exists(executable)) return (false, "VLC wurde nicht gefunden. Bitte VLC wählen.");
        if (!WebAddress.TryHttp(source.Url, out _)) return (false, "Ungültige Streamadresse.");
        LastConfirmedTime = 0; LastDecodedVideo = 0; LastDecodedAudio = 0; LastStatusJson = "";
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var arg in new[] { "--no-one-instance", "--no-playlist-enqueue", "--no-video-title-show", "--no-media-library",
            "--extraintf=http", "--http-host=127.0.0.1", $"--http-port={port}", $"--http-password={password}" }) start.ArgumentList.Add(arg);
        if (DiagnosticMode)
        {
            start.WindowStyle = ProcessWindowStyle.Hidden;
            foreach (var arg in new[] { "--intf=dummy", "--vout=dummy", "--aout=dummy" }) start.ArgumentList.Add(arg);
        }
        if (!live && double.IsFinite(seconds) && seconds > 0)
            start.ArgumentList.Add("--start-time=" + seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        if (WebAddress.TryHttp(source.Referer, out _)) start.ArgumentList.Add("--http-referrer=" + source.Referer);
        start.ArgumentList.Add("--"); start.ArgumentList.Add(source.Url);
        // VLC's UI sets its application agent after reading global arguments.
        // A media-scoped option survives that initialization and follows this URL only.
        if (source.UserAgent.Length > 0 && !source.UserAgent.Any(char.IsControl)) start.ArgumentList.Add(":http-user-agent=" + source.UserAgent);
        Process? process;
        try { process = Process.Start(start); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { return (false, "VLC konnte nicht gestartet werden: " + e.Message); }
        if (process is null) return (false, "VLC konnte nicht gestartet werden.");
        var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + password)));
        sessions.Add((process, client)); var watch = Stopwatch.StartNew(); bool confirmed = false;
        try
        {
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                ct.ThrowIfCancellationRequested();
                if (process.HasExited) return (false, "VLC wurde beendet. Browservideo bleibt aktiv.");
                try
                {
                    using var response = await client.GetAsync($"http://127.0.0.1:{port}/requests/status.json", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        LastStatusJson = await response.Content.ReadAsStringAsync(ct);
                        using var json = JsonDocument.Parse(LastStatusJson);
                        if (json.RootElement.TryGetProperty("state", out var state) && state.GetString() == "playing" &&
                            json.RootElement.TryGetProperty("stats", out var stats) && stats.TryGetProperty("decodedvideo", out var frames) && frames.GetInt64() > 0)
                        {
                            LastDecodedVideo = frames.GetInt64();
                            LastConfirmedTime = json.RootElement.TryGetProperty("time", out var time) ? time.GetDouble() : 0;
                            LastDecodedAudio = stats.TryGetProperty("decodedaudio", out var audio) ? audio.GetInt64() : 0;
                            if (!live && seconds > 1 && LastConfirmedTime + 1 < seconds)
                            { await Task.Delay(250, ct); continue; }
                            confirmed = true; return (true, "VLC spielt. Browserstream wurde freigegeben.");
                        }
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
                catch (JsonException) { }
                await Task.Delay(250, ct);
            }
            return (false, "VLC meldet nach 15 Sekunden keine Wiedergabe. Browservideo bleibt aktiv.");
        }
        finally
        {
            if (!confirmed) try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        }
    }
    public void Dispose()
    {
        foreach (var (process, client) in sessions) { client.Dispose(); process.Dispose(); }
        sessions.Clear();
    }
    public void StopDiagnosticSessions()
    {
        if (!DiagnosticMode) return;
        foreach (var (process, _) in sessions) try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
    }
}
