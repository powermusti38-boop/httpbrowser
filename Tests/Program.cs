using StreamBrowser.Core;
using StreamBrowser.Diagnostics;
using System.Text.Json;

var fixtures = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "fixtures");
using var server = new FixtureServer(fixtures);
using var probe = new StreamProbe();
var failures = new List<string>();
async Task Check(string path, bool expected, string kind = "", bool? live = null)
{
    var result = await probe.CheckAsync(new(server.BaseUrl + path, server.BaseUrl + "/fixture", "FixtureAgent/1.0"));
    bool ok = result.Success == expected && (!expected || result.Kind == kind) && (live == null || result.IsLive == live);
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {path}: {result.Message}");
    if (!ok) failures.Add(path);
}
await Check("/sample.mp4", true, "mp4", false);
await Check("/redirect.mp4", true, "mp4");
await Check("/headers.mp4", true, "mp4");
await Check("/vod.m3u8", true, "hls", false);
await Check("/live.m3u8", true, "hls", true);
await Check("/master.m3u8", true, "hls", false);
await Check("/secure/vod.m3u8", true, "hls", false);
await Check("/denied.mp4", false);
await Check("/html.mp4", false);
await Check("/badsegment.m3u8", false);
await Check("/drm.m3u8", false);
await Check("/cycle.m3u8", false);
var invalid = await probe.CheckAsync(new("file:///C:/Windows/win.ini", "", ""));
if (invalid.Success) failures.Add("file scheme accepted");
using (var cancel = new CancellationTokenSource(100))
{
    try { await probe.CheckAsync(new(server.BaseUrl + "/slow.mp4", "", ""), cancel.Token); failures.Add("Cancellation not propagated"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS cancellation"); }
}
using var launcher = new VlcLauncher();
var missing = await launcher.PlayAsync("C:/does-not-exist/vlc.exe", new(server.BaseUrl + "/sample.mp4", "", ""), 5, false, CancellationToken.None);
if (missing.Success) failures.Add("Missing VLC accepted");
if (args.Contains("--vlc"))
{
    launcher.DiagnosticMode = true;
    var executable = VlcLauncher.FindInstalled(null);
    if (executable is null) failures.Add("VLC not installed");
    else
    {
        var played = await launcher.PlayAsync(executable, new(server.BaseUrl + "/headers.mp4", server.BaseUrl + "/fixture", "FixtureAgent/1.0"), 5, false, CancellationToken.None);
        bool ok = played.Success && launcher.LastDecodedVideo > 0 && launcher.LastDecodedAudio > 0 && launcher.LastConfirmedTime >= 4;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} VLC: required Referer/User-Agent, video/audio decode, seek; {played.Message}");
        if (!ok) foreach (var r in server.Requests.Where(r=>r.Path=="/headers.mp4")) Console.WriteLine($"HEADER CHECK: agent={r.Agent}; referer={r.Referer}");
        if (!ok) failures.Add("VLC headers/audio/seek");
        var hls = await launcher.PlayAsync(executable, new(server.BaseUrl + "/secure/vod.m3u8", server.BaseUrl + "/fixture", "FixtureAgent/1.0"), 0, false, CancellationToken.None);
        Console.WriteLine($"{(hls.Success ? "PASS" : "FAIL")} VLC HLS: required headers on manifest and segments; {hls.Message}");
        if (!hls.Success)
        {
            failures.Add("VLC HLS headers");
            foreach (var r in server.Requests.Where(r=>r.Path.StartsWith("/secure/"))) Console.WriteLine($"HLS HEADER CHECK: agent={r.Agent}; referer={r.Referer}");
        }
    }
    launcher.StopDiagnosticSessions();
}
Console.WriteLine(JsonSerializer.Serialize(new { success = failures.Count == 0, failures }));
return failures.Count == 0 ? 0 : 1;
