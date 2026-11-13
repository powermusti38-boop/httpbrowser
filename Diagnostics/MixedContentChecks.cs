using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace StreamBrowser.Diagnostics;

// HTTPS documents are supplied through WebResourceRequested. HTTP media comes
// from a different, non-loopback origin so Chromium's mixed-content rules apply.
internal sealed class MixedContentChecks(string fixtures) : IDisposable
{
    internal const string PageUrl = "https://streambrowser-mixed.test/player";
    private const string FrameHost = "streambrowser-frame.test";
    private const string MediaHost = "streambrowser-media.test";
    private readonly List<(CoreWebView2 Core, EventHandler<CoreWebView2WebResourceRequestedEventArgs> Handler)> handlers = [];
    private readonly List<MemoryStream> streams = [];
    private readonly List<string> mediaRequests = [];

    private void Attach(CoreWebView2 core)
    {
        void Respond(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var uri = new Uri(args.Request.Uri);
            if (uri.Host is not ("streambrowser-mixed.test" or FrameHost or MediaHost)) return;
            byte[] bytes;
            string type;
            int status = 200;
            if (uri.Host == MediaHost)
            {
                mediaRequests.Add(uri.AbsoluteUri);
                string file = uri.AbsolutePath.TrimStart('/');
                if (uri.Scheme != "http" || (file != "vod.m3u8" && file is not ("chunk000.ts" or "chunk001.ts" or "chunk002.ts")))
                {
                    bytes = []; status = 404;
                }
                else bytes = File.ReadAllBytes(Path.Combine(fixtures, file));
                type = file.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : "video/mp2t";
            }
            else if (uri.AbsolutePath == "/hls.min.js")
            {
                bytes = File.ReadAllBytes(Path.Combine(fixtures, "hls.min.js"));
                type = "application/javascript";
            }
            else
            {
                string frame = uri.Host == FrameHost ? "" : $"<iframe src='https://{FrameHost}/player' style='width:760px;height:480px'></iframe>";
                bytes = Encoding.UTF8.GetBytes("<!doctype html><meta charset='utf-8'><title>HTTPS / HTTP stream test</title>" +
                    "<style>body{background:#10232f;color:white;font:18px system-ui}video{width:640px;height:360px}</style>" +
                    "<h1>HTTPS-Seite mit HTTP-HLS</h1><video controls muted></video>" + frame +
                    "<script src='/hls.min.js'></script><script>window.mixedResult='pending';" +
                    $"fetch('http://{MediaHost}/vod.m3u8').then(r=>{{if(!r.ok)throw Error(r.status);return r.text()}}).then(()=>{{" +
                    "const v=document.querySelector('video');window.hls=new Hls();" +
                    $"hls.loadSource('http://{MediaHost}/vod.m3u8');hls.attachMedia(v);" +
                    "hls.on(Hls.Events.MANIFEST_PARSED,()=>v.play().catch(()=>{}));window.mixedResult='loaded';" +
                    "}).catch(()=>window.mixedResult='blocked');</script>");
                type = "text/html; charset=utf-8";
            }
            var stream = new MemoryStream(bytes);
            streams.Add(stream);
            args.Response = core.Environment.CreateWebResourceResponse(stream, status, status == 200 ? "OK" : "Not Found",
                $"Content-Type: {type}\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store\r\n");
        }
        core.WebResourceRequested += Respond;
        handlers.Add((core, Respond));
    }

    internal static async Task RunAsync(MainWindow main, string fixtures, string output)
    {
        using var checks = new MixedContentChecks(fixtures);
        checks.Attach(main.Core);
        main.DiagnosticNavigate(PageUrl);
        async Task Wait(Func<Task<bool>> condition, string message)
        {
            for (int i = 0; i < 150; i++)
            {
                if (await condition()) return;
                await Task.Delay(100);
            }
            throw new InvalidOperationException(message);
        }
        async Task<bool> PlayersMatch(MainWindow window, string expression)
        {
            if (window.Documents.Count(d => new Uri(d.FrameUrl).AbsolutePath == "/player") != 2) return false;
            foreach (var doc in window.Documents.ToArray())
                if (await doc.Execute(expression) != "true") return false;
            return true;
        }
        const string playing = "location.protocol==='https:' && isSecureContext && window.mixedResult==='loaded' && document.querySelector('video').getVideoPlaybackQuality().totalVideoFrames>0";
        await Wait(() => PlayersMatch(main, playing), "Main window did not decode HTTP HLS on HTTPS in both documents");
        if (main.PageWindows.Count != 0) throw new InvalidOperationException("HTTP playback required another window");
        if (!main.Documents.All(d => d.Sources.ContainsKey($"http://{MediaHost}/vod.m3u8")))
            throw new InvalidOperationException("HTTP manifests were not discovered for VLC");
        await using (var screenshot = File.Create(Path.Combine(output, "http-browser-playing.png")))
            await main.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);

        // A source window inherits the very same HTTP-enabled browser environment.
        await main.Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
        {
            expression = "window.open(" + JsonSerializer.Serialize(PageUrl) + ")", userGesture = true
        }));
        await Wait(() => Task.FromResult(main.PageWindows.Count == 1 && main.PageWindows[0].BrowserReady), "Source window did not initialize");
        var popup = main.PageWindows[0];
        try
        {
            checks.Attach(popup.Core);
            popup.DiagnosticNavigate(PageUrl);
            await Wait(() => PlayersMatch(popup, playing), "Source window did not decode HTTP HLS on HTTPS in both documents");
            if (!checks.mediaRequests.Any(u => u.StartsWith("http://") && u.EndsWith(".ts")))
                throw new InvalidOperationException("No actual HTTP HLS segments requested");
            foreach (string other in new[] { $"https://{FrameHost}/player", $"http://{FrameHost}/player" })
            {
                main.DiagnosticNavigate(other);
                await Wait(async () => main.Core.Source == other && main.Documents.Count == 1 &&
                    await main.Core.ExecuteScriptAsync("window.mixedResult==='loaded' && document.querySelector('video').getVideoPlaybackQuality().totalVideoFrames>0") == "true",
                    "Main window could not navigate and play media on another HTTP/HTTPS origin");
            }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                success = true,
                passed = new[]
                {
                    "Main window decodes HTTP HLS in HTTPS page and cross-origin iframe without a mode switch",
                    "HTTP HLS sources remain discoverable for independent verification and VLC",
                    "Source window inherits HTTP playback in HTTPS page and cross-origin iframe",
                    "Main window can navigate to other HTTP/HTTPS origins and continue playback"
                },
                mediaRequests = checks.mediaRequests
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            // Detach handlers before disposing the source WebView.
            checks.Dispose();
            popup.Close();
        }
    }

    public void Dispose()
    {
        foreach (var (core, handler) in handlers) core.WebResourceRequested -= handler;
        handlers.Clear();
        foreach (var stream in streams) stream.Dispose();
        streams.Clear();
    }
}
