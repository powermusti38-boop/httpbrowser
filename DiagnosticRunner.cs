using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using StreamBrowser.Diagnostics;

namespace StreamBrowser;

internal static class DiagnosticRunner
{
    internal static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "diagnostics");
    internal static void WriteFatal(Exception exception)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "result.json"), JsonSerializer.Serialize(new { success = false, error = exception.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static async Task RunAsync(MainWindow window)
    {
        Directory.CreateDirectory(DirectoryPath);
        var arguments = Environment.GetCommandLineArgs();
        int fixtureIndex = Array.IndexOf(arguments, "--fixture-dir");
        string fixtures = fixtureIndex >= 0 ? arguments[fixtureIndex + 1] : Path.Combine(AppContext.BaseDirectory, "fixtures");
        using var server = new FixtureServer(fixtures);
        var passed = new List<string>(); var notes = new List<string>();
        async Task Wait(Func<bool> condition, string message, int seconds = 20)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition())
            {
                if (DateTime.UtcNow >= until) throw new InvalidOperationException(message + "; status=" + window.LastHandoffError);
                await Task.Delay(100);
            }
        }
        async Task<StreamBrowser.Browser.BrowserSession> Load(string path, string framePath)
        {
            var previousIds = window.Documents.Select(d => d.Id).ToHashSet();
            window.DiagnosticNavigate(server.BaseUrl + path);
            await Wait(() => window.Documents.Any(d => !previousIds.Contains(d.Id) && new Uri(d.FrameUrl).AbsolutePath == framePath), "Document bridge missing: " + path);
            var doc = window.Documents.First(d => !previousIds.Contains(d.Id) && new Uri(d.FrameUrl).AbsolutePath == framePath);
            await Task.Delay(1000);
            return doc;
        }
        async Task Inspect(StreamBrowser.Browser.BrowserSession doc, string expression = "document.querySelector('video')")
        {
            await doc.Execute($"window.__streamBrowserBridge.inspect({expression})");
            await Wait(() => doc.Allowed.Count > 0, "No verified stream: " + doc.FrameUrl);
            await Task.Delay(300);
        }
        async Task Handoff(StreamBrowser.Browser.BrowserSession doc, string? url = null)
        {
            int count = window.SuccessfulHandoffs, acknowledgements = window.StopAcknowledgements;
            window.DiagnosticEvents.Add("before-activate:" + await doc.Execute("JSON.stringify(window.__streamBrowserBridge.snapshot())"));
            await doc.Execute("window.__streamBrowserBridge.activate(" + JsonSerializer.Serialize(url) + ")");
            await Wait(() => window.SuccessfulHandoffs > count, "VLC handoff failed", 23);
            await Wait(() => window.StopAcknowledgements > acknowledgements, "Browser did not acknowledge stop");
            string snapshot = await doc.Execute("JSON.stringify(window.__streamBrowserBridge.snapshot())");
            if (!snapshot.Contains("stopped\\\":true")) throw new InvalidOperationException("Browser video was not released: " + snapshot);
        }
        async Task Screenshot(string file)
        {
            await using var output = File.Create(Path.Combine(DirectoryPath, file));
            await window.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
        }
        async Task<string> EvaluateAsync(StreamBrowser.Browser.BrowserSession doc, string expression)
        {
            string key = "__test_" + Guid.NewGuid().ToString("N");
            await doc.Execute($"window.{key}=null;Promise.resolve({expression}).then(v=>window.{key}=JSON.stringify(v)).catch(e=>window.{key}=JSON.stringify({{error:String(e)}}));'started'");
            for (int i=0;i<50;i++)
            {
                var value = await doc.Execute("window." + key);
                if (value != "null") return JsonSerializer.Deserialize<string>(value)!;
                await Task.Delay(100);
            }
            throw new InvalidOperationException("Async page check timed out: " + expression);
        }
        try
        {
            if (arguments.Contains("--popup-only"))
            {
                await PopupChecks.RunAsync(window, server, DirectoryPath);
                Application.Current.Shutdown(0);
                return;
            }
            if (arguments.Contains("--vavoo-item-test"))
            {
                await ItemPopupChecks.RunAsync(window, DirectoryPath);
                Application.Current.Shutdown(0);
                return;
            }
            if (arguments.Contains("--vavoo-test"))
            {
                await VavooChecks.RunAsync(window, DirectoryPath);
                Application.Current.Shutdown(0);
                return;
            }
            if (arguments.Contains("--mixed-content-only"))
            {
                await MixedContentChecks.RunAsync(window, fixtures, DirectoryPath);
                Application.Current.Shutdown(0);
                return;
            }
            if (arguments.Contains("--hls-only"))
            {
                var hlsDoc = await Load("/hls", "/hls");
                var decoded = await EvaluateAsync(hlsDoc,"new Promise(resolve=>{const check=setInterval(()=>{const v=document.querySelector('video');if(!v.paused&&v.getVideoPlaybackQuality().totalVideoFrames>0){clearInterval(check);resolve(v.getVideoPlaybackQuality().totalVideoFrames)}},100)})");
                if(!int.TryParse(decoded,out var frames)||frames<=0)throw new InvalidOperationException("Browser did not decode actual HLS");
                await Inspect(hlsDoc); await Screenshot("hls-browser-playing.png"); await Handoff(hlsDoc);
                await Task.Delay(800); var stoppedAt=DateTime.UtcNow; await Task.Delay(1800);
                if(server.Requests.Any(r=>r.At>stoppedAt && r.Path.EndsWith(".ts") && r.IsBrowser))throw new InvalidOperationException("HLS browser kept loading media");
                passed.Add("Real hls.js player: browser decodes HLS, green hover, VLC decodes HLS, browser source released and further segments stopped");
            }
            if (!arguments.Contains("--site-only") && !arguments.Contains("--hls-only"))
            {
            var doc = await Load("/fixture", "/fixture");
            await doc.Execute("document.querySelector('video').currentTime=5;document.querySelector('video').play().catch(()=>{})");
            await Inspect(doc); await Screenshot("mp4-ready.png");
            // A web page cannot request arbitrary commands or bypass the host-issued handoff token.
            await doc.Execute("chrome.webview.postMessage({type:'handoff',doc:window.__streamBrowserBridge.snapshot().doc,frameUrl:location.href,id:'fake',url:'file:///C:/Windows/win.ini',token:'fake'})");
            await Task.Delay(300);
            if (window.SuccessfulHandoffs != 0) throw new InvalidOperationException("Forged command accepted");
            await Handoff(doc);
            if (window.DiagnosticVlc.LastConfirmedTime < 4 || window.DiagnosticVlc.LastDecodedVideo <= 0) throw new InvalidOperationException("VLC did not seek/decode video: " + window.DiagnosticVlc.LastConfirmedTime + " / " + window.DiagnosticVlc.LastStatusJson);
            await Screenshot("mp4-transferred.png");
            var blocked = await EvaluateAsync(doc, "fetch('/sample.mp4').then(()=>false).catch(()=>true)");
            if (blocked != "true") throw new InvalidOperationException("Transferred source still fetchable");
            var unrelated = await EvaluateAsync(doc, "fetch('/ping').then(r=>r.text())");
            if (!unrelated.Contains("ok")) throw new InvalidOperationException("Unrelated page request blocked");
            int nativeBlocks = window.BlockedRequests;
            await doc.Execute("window.__retryWorker=new Worker(URL.createObjectURL(new Blob([\"fetch('\"+location.origin+\"/sample.mp4',{cache:'reload'}).then(r=>postMessage(r.status))\"],{type:'application/javascript'})))");
            await Wait(() => window.BlockedRequests > nativeBlocks, "Native browser request not blocked");
            await doc.Execute("window.__retryWorker.terminate()");
            passed.Add("MP4: hover, green marker, VLC decode, seek >=4s, browser release, media block, unrelated requests, forged message");

            doc = await Load("/overlay", "/overlay");
            await doc.Execute("(()=>{const c=document.getElementById('cover'),r=c.getBoundingClientRect();c.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,clientX:r.left+r.width/2,clientY:r.top+r.height/2}));})()");
            await Wait(()=>doc.Allowed.Count>0,"Video beneath player controls not detected");
            await Task.Delay(300); await Handoff(doc);
            passed.Add("Hover over a player control overlay: underlying video recognized and handed off");

            doc = await Load("/frame", "/fixture"); await Inspect(doc); await Handoff(doc);
            passed.Add("Embedded frame: bridge, source detection and actual VLC playback");

            doc = await Load("/multi", "/multi"); await Inspect(doc);
            if (doc.Allowed.Count != 1 || doc.Allowed.Single().Contains("other")) throw new InvalidOperationException("Direct video source mixed with neighboring video");
            await Handoff(doc);
            var other = await doc.Execute("document.getElementById('other').getAttribute('src')");
            if (!other.Contains("other=1")) throw new InvalidOperationException("Neighbor video released");
            passed.Add("Multiple MP4 videos: selected video only");

            doc = await Load("/blob", "/blob"); await Inspect(doc);
            if (doc.Allowed.Count < 2) throw new InvalidOperationException("Ambiguous HLS candidates not offered");
            int before = window.SuccessfulHandoffs;
            await doc.Execute("window.__streamBrowserBridge.activate(null)"); await Task.Delay(400);
            if (window.SuccessfulHandoffs != before) throw new InvalidOperationException("Ambiguous stream auto selected");
            await Screenshot("hls-selection.png");
            await Handoff(doc, server.BaseUrl + "/vod.m3u8");
            passed.Add("Blob player: multiple HLS sources require selection; selected HLS decoded in VLC");

            doc = await Load("/hls", "/hls"); await Inspect(doc); await Handoff(doc);
            await Task.Delay(800); var stoppedAt = DateTime.UtcNow; await Task.Delay(1800);
            if (server.Requests.Any(r => r.At > stoppedAt && r.Path == "/chunk000.ts" && r.IsBrowser))
                throw new InvalidOperationException("Browser kept loading HLS after transfer");
            passed.Add("Live HLS: VLC playback and repeated browser segment loading stopped");

            doc = await Load("/fixture", "/fixture");
            await doc.Execute("document.querySelector('video').src='/denied.mp4';window.__streamBrowserBridge.inspect(document.querySelector('video'))");
            await Task.Delay(1800);
            if (doc.Allowed.Count != 0) throw new InvalidOperationException("403 source marked ready");
            var source = await doc.Execute("document.querySelector('video').getAttribute('src')");
            if (!source.Contains("denied.mp4")) throw new InvalidOperationException("Failed source removed from browser");
            passed.Add("Unavailable source: not green, browser source retained");

            doc = await Load("/fixture", "/fixture");
            await doc.Execute("document.querySelector('video').src='/invalidcodec.mp4';document.querySelector('video').load()");
            await Task.Delay(300); await Inspect(doc);
            int priorHandoffs = window.SuccessfulHandoffs, priorStops = window.StopAcknowledgements;
            await doc.Execute("window.__streamBrowserBridge.activate(null)");
            await Wait(() => window.LastHandoffError.Length > 0, "Invalid codec did not report VLC failure", 23);
            if (window.SuccessfulHandoffs != priorHandoffs || window.StopAcknowledgements != priorStops) throw new InvalidOperationException("Failed VLC handoff stopped browser");
            if (!(await doc.Execute("document.querySelector('video').getAttribute('src')")).Contains("invalidcodec.mp4")) throw new InvalidOperationException("Failed VLC handoff detached source");
            passed.Add("VLC cannot decode: timeout/error leaves browser source intact and terminates only the failed owned session");
            }

            if (arguments.Contains("--site-test"))
            {
                window.DiagnosticNavigate("https://taraftarium.co/");
                await Task.Delay(12000);
                foreach (var item in window.Documents.ToArray().Where(d => new Uri(d.FrameUrl).AbsolutePath.EndsWith("event.html")))
                {
                    await item.Execute("(()=>{const v=document.getElementById('preroll-video');if(v)v.muted=true;const b=document.getElementById('ad-play-btn');if(b&&b.getBoundingClientRect().width>0)b.click();new MutationObserver(()=>document.querySelectorAll('video').forEach(v=>v.muted=true)).observe(document.documentElement,{subtree:true,childList:true});})()");
                }
                await Task.Delay(6500);
                foreach (var item in window.Documents.ToArray().Where(d => new Uri(d.FrameUrl).AbsolutePath.EndsWith("event.html")))
                {
                    await item.Execute("[...document.querySelectorAll('button,div,span')].find(e=>e.childElementCount===0 && /Reklamı Geç/i.test(e.textContent) && e.getBoundingClientRect().width>0)?.click()");
                }
                await Task.Delay(8000);
                foreach (var item in window.Documents.ToArray().Where(d => d.Sources.Keys.Any(u=>new Uri(u).AbsolutePath.EndsWith(".m3u8",StringComparison.OrdinalIgnoreCase)) || d.Sources.Values.Any(t=>t.Contains("mpegurl",StringComparison.OrdinalIgnoreCase))))
                {
                    try
                    {
                        await item.Execute("window.__streamBrowserBridge.inspect([...document.querySelectorAll('video')].filter(v=>v.getBoundingClientRect().width>0 && v.getBoundingClientRect().height>0).sort((a,b)=>b.clientWidth*b.clientHeight-a.clientWidth*a.clientHeight)[0])");
                        await Task.Delay(5000);
                        if (item.Allowed.Count > 0)
                        {
                            await Handoff(item,item.Allowed.First());
                            passed.Add("Candidate site: live source confirmed playing in VLC");
                            break;
                        }
                    }
                    catch (Exception ex) { notes.Add("Candidate handoff: " + ex.Message + "; decoded video=" + window.DiagnosticVlc.LastDecodedVideo + "; decoded audio=" + window.DiagnosticVlc.LastDecodedAudio); }
                }
                await Screenshot("candidate-site.png");
                var siteInfo = new List<object>();
                foreach (var item in window.Documents.ToArray())
                {
                    try
                    {
                        string state = await item.Execute("JSON.stringify({title:document.title,url:location.href,videos:[...document.querySelectorAll('video')].map(v=>({src:v.currentSrc||v.src,visible:v.getBoundingClientRect().height>0,error:v.error?.message})),bridge:!!window.__streamBrowserBridge,text:document.body?.innerText.slice(0,500)})");
                        siteInfo.Add(new { frame = item.FrameUrl, sources = item.Sources.Keys.Select(u=>new Uri(u).GetLeftPart(UriPartial.Path)), verified = item.Allowed.Select(u=>new Uri(u).GetLeftPart(UriPartial.Path)), state });
                    }
                    catch (Exception ex) { notes.Add(ex.Message); }
                }
                File.WriteAllText(Path.Combine(DirectoryPath, "candidate-site.json"), JsonSerializer.Serialize(siteInfo, new JsonSerializerOptions { WriteIndented = true }));
                notes.Add("Candidate site loaded and inspected. No playback guarantee without a reachable active video source and a confirmed VLC handoff.");
            }
            File.WriteAllText(Path.Combine(DirectoryPath, "result.json"), JsonSerializer.Serialize(new { success = true, passed, notes, handoffs = window.SuccessfulHandoffs, blockedRequests = window.BlockedRequests }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            await Screenshot("failure.png");
            File.WriteAllText(Path.Combine(DirectoryPath, "result.json"), JsonSerializer.Serialize(new { success = false, passed, error = ex.ToString(), events = window.DiagnosticEvents }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(1);
        }
    }
}
