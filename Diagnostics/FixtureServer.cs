using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace StreamBrowser.Diagnostics;

internal sealed class FixtureServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly string directory;
    public string BaseUrl { get; }
    public ConcurrentQueue<(DateTime At, string Path, string Agent, string Referer, bool IsBrowser)> Requests { get; } = new();
    public FixtureServer(string directory)
    {
        this.directory = directory;
        var portFinder = new TcpListener(IPAddress.Loopback, 0); portFinder.Start();
        int port = ((IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
        BaseUrl = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(BaseUrl + "/");
        listener.Prefixes.Add(BaseUrl.Replace("127.0.0.1", "localhost") + "/");
        listener.Start(); _ = ListenAsync();
    }
    private async Task ListenAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try { var context = await listener.GetContextAsync().WaitAsync(lifetime.Token); _ = RespondAsync(context); }
            catch (Exception e) when (e is OperationCanceledException or HttpListenerException or ObjectDisposedException) { break; }
        }
    }
    private async Task RespondAsync(HttpListenerContext context)
    {
        var request = context.Request; var response = context.Response;
        string path = request.Url!.AbsolutePath;
        Requests.Enqueue((DateTime.UtcNow, path, request.UserAgent ?? "", request.Headers["Referer"] ?? "", request.Headers["Sec-Fetch-Mode"] is not null));
        try
        {
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Accept-Ranges"] = "bytes";
            if (path == "/denied.mp4") { response.StatusCode = 403; return; }
            if (path == "/redirect.mp4") { response.Redirect(BaseUrl + "/sample.mp4"); return; }
            if (path == "/popup-redirect") { response.Redirect(BaseUrl.Replace("127.0.0.1", "localhost") + "/fixture"); return; }
            if (path == "/slow.mp4") { await Task.Delay(30000, lifetime.Token); return; }
            if ((path == "/headers.mp4" || path.StartsWith("/secure/")) && (request.UserAgent != "FixtureAgent/1.0" || request.Headers["Referer"] != BaseUrl + "/fixture"))
            { response.StatusCode = 403; return; }
            string? text = path switch
            {
                "/fixture" => Page("<video id='main' controls muted preload='auto' src='/sample.mp4'></video>"),
                "/popup-auto" => Page("<script>setTimeout(()=>window.open('/fixture'),300)</script>"),
                "/popup-script-redirect" => "<!doctype html><title>Redirecting...</title><script>setTimeout(()=>location.replace('" + BaseUrl.Replace("127.0.0.1", "localhost") + "/fixture'),300)</script>",
                "/overlay" => Page("<div style='position:relative;width:720px;height:430px'><video id='main' controls muted src='/sample.mp4'></video><div id='cover' style='position:absolute;inset:15px 0 65px;background:#ffffff08'>Player-Steuerung über dem Video</div></div>"),
                "/frame" => Page("<h1>Player im eingebetteten Frame</h1><iframe src='" + BaseUrl.Replace("127.0.0.1", "localhost") + "/fixture' style='width:760px;height:480px;border:0'></iframe>"),
                "/multi" => Page("<video id='main' controls muted src='/sample.mp4'></video><video id='other' controls muted src='/sample.mp4?other=1' style='width:300px'></video>"),
                "/blob" => Page("<video id='main' controls muted></video><script>const v=document.querySelector('video');v.src=URL.createObjectURL(new MediaSource());Promise.all([fetch('/live.m3u8'),fetch('/vod.m3u8')]);</script>"),
                "/hls" => Page("<video id='main' controls muted></video><script src='/hls.min.js'></script><script>const v=document.querySelector('video');window.hls=new Hls({maxBufferLength:4,liveSyncDurationCount:1});hls.loadSource('/live.m3u8');hls.attachMedia(v);hls.on(Hls.Events.MANIFEST_PARSED,()=>v.play().catch(()=>{}));window.poll=setInterval(()=>fetch('/chunk000.ts').catch(()=>{}),350);</script>"),
                "/live.m3u8" => ReadManifest(false),
                "/vod.m3u8" => ReadManifest(true),
                "/secure/vod.m3u8" => ReadManifest(true),
                "/master.m3u8" => "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=500000\nvod.m3u8\n",
                "/drm.m3u8" => "#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI=\"key\",KEYFORMAT=\"com.apple.streamingkeydelivery\"\n#EXTINF:2,\nchunk000.ts\n",
                "/badsegment.m3u8" => "#EXTM3U\n#EXTINF:2,\nmissing.ts\n",
                "/cycle.m3u8" => "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=500000\ncycle.m3u8\n",
                "/html.mp4" => "<html>Login required</html>",
                "/ping" => "ok",
                _ => null
            };
            byte[] bytes;
            if (path == "/invalidcodec.mp4")
            {
                bytes = new byte[] { 0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0, 105, 115, 111, 109, 109, 112, 52, 49 };
                response.ContentType = "video/mp4";
            }
            else if (text is not null)
            {
                bytes = Encoding.UTF8.GetBytes(text);
                response.ContentType = path.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "text/html; charset=utf-8";
            }
            else
            {
                var file = path is "/headers.mp4" ? "sample.mp4" : Path.GetFileName(path);
                var full = Path.Combine(directory, file);
                if (!File.Exists(full)) { response.StatusCode = 404; return; }
                bytes = await File.ReadAllBytesAsync(full, lifetime.Token);
                response.ContentType = file.EndsWith(".js") ? "application/javascript" : file.EndsWith(".ts") ? "video/mp2t" : "video/mp4";
            }
            int from = 0, to = bytes.Length - 1;
            var range = request.Headers["Range"];
            if (range?.StartsWith("bytes=") == true)
            {
                var parts = range[6..].Split('-');
                if (int.TryParse(parts[0], out int begin)) from = begin;
                if (parts.Length > 1 && int.TryParse(parts[1], out int end)) to = Math.Min(end, to);
                if (from >= bytes.Length || from > to) { response.StatusCode = 416; return; }
                response.StatusCode = 206; response.Headers["Content-Range"] = $"bytes {from}-{to}/{bytes.Length}";
            }
            response.ContentLength64 = to - from + 1;
            await response.OutputStream.WriteAsync(bytes.AsMemory(from, to - from + 1), lifetime.Token);
        }
        catch (Exception e) when (e is IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException) { }
        finally { response.Close(); }
    }
    private string ReadManifest(bool vod)
    {
        var text = File.ReadAllText(Path.Combine(directory, "vod.m3u8"));
        return vod ? text : text.Replace("#EXT-X-ENDLIST", "").Replace("#EXT-X-PLAYLIST-TYPE:VOD", "");
    }
    private static string Page(string body) => "<!doctype html><html><head><meta charset='utf-8'><title>StreamBrowser Test</title><style>body{margin:35px;background:#10232f;color:white;font:18px system-ui}video{display:block;width:720px;height:405px;background:#000;margin:15px 0}h1{font-size:24px}</style></head><body><h1>StreamBrowser · kontrollierter Wiedergabetest</h1>" + body + "</body></html>";
    public void Dispose() { lifetime.Cancel(); listener.Close(); lifetime.Dispose(); }
}
