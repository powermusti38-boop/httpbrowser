using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace StreamBrowser.Diagnostics;

internal static class VavooChecks
{
    private const string Url = "https://vavoo.to/#/channels";
    internal static async Task RunAsync(MainWindow normal, string output)
    {
        var results = new List<object>();
        bool success = true;
        try
        {
            var windows = new[] { (normal, "http") };
            foreach (var (window, name) in windows)
            {
                var events = new List<string>();
                var requests = new List<object>();
                window.Core.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived += (_, args) =>
                {
                    using var json = JsonDocument.Parse(args.ParameterObjectAsJson);
                    var response = json.RootElement.GetProperty("response");
                    string url = response.GetProperty("url").GetString()!;
                    if (new Uri(url).Scheme == "http" || response.GetProperty("status").GetDouble() >= 400)
                        requests.Add(new { url, status = response.GetProperty("status").GetDouble(), mime = response.GetProperty("mimeType").GetString() });
                };
                foreach (string method in new[] { "Log.entryAdded", "Network.loadingFailed" })
                    window.Core.GetDevToolsProtocolEventReceiver(method).DevToolsProtocolEventReceived += (_, args) => events.Add(args.ParameterObjectAsJson);
                await window.Core.CallDevToolsProtocolMethodAsync("Log.enable", "{}");
                await window.Core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                window.DiagnosticNavigate(Url);
                await Task.Delay(14000);
                await window.Core.ExecuteScriptAsync("(()=>{const input=document.querySelector('input[placeholder=\"Kanäle suchen...\"]');if(!input)throw Error('Channel search missing');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'ZDF');input.dispatchEvent(new Event('input',{bubbles:true}));})()");
                await Task.Delay(3500);
                string channels = await window.Core.ExecuteScriptAsync("JSON.stringify([...document.querySelectorAll('.ch-row')].map(e=>e.getAttribute('aria-label')))");
                File.WriteAllText(Path.Combine(output, $"vavoo-{name}-channels.json"), JsonSerializer.Deserialize<string>(channels));
                await window.Core.ExecuteScriptAsync("(()=>{const row=[...document.querySelectorAll('.ch-row')].find(e=>e.getAttribute('aria-label')==='ZDF HD (1)');if(!row)throw Error('ZDF channel missing');row.click();})()");
                for (int i = 0; i < 90; i++)
                {
                    await Task.Delay(500);
                    if (await window.Core.ExecuteScriptAsync("[...document.querySelectorAll('video')].some(v=>v.getVideoPlaybackQuality().totalVideoFrames>5)") == "true") break;
                }
                string overview = await window.Core.ExecuteScriptAsync("JSON.stringify({url:location.href,text:document.body.innerText.slice(0,9000),videos:[...document.querySelectorAll('video')].map(v=>({src:v.currentSrc,paused:v.paused,time:v.currentTime,ready:v.readyState,frames:v.getVideoPlaybackQuality().totalVideoFrames,error:v.error?.message})),elements:[...document.querySelectorAll('button,a,[role=button],input')].slice(0,120).map(e=>({tag:e.tagName,cls:e.className,text:e.textContent.slice(0,100),href:e.getAttribute('href'),placeholder:e.getAttribute('placeholder')}))})");
                string state = JsonSerializer.Deserialize<string>(overview)!;
                File.WriteAllText(Path.Combine(output, $"vavoo-{name}-overview.json"), state);
                using var parsed = JsonDocument.Parse(state);
                bool browserPlayback = parsed.RootElement.GetProperty("videos").EnumerateArray().Any(v => v.GetProperty("frames").GetInt32() > 0 && v.GetProperty("time").GetDouble() > 0);
                success &= browserPlayback;
                await using (var image = File.Create(Path.Combine(output, $"vavoo-{name}.png")))
                    await window.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                File.WriteAllText(Path.Combine(output, $"vavoo-{name}-events.json"), JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(output, $"vavoo-{name}-requests.json"), JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true }));
                var doc = window.Documents.FirstOrDefault(d => d.FrameUrl.StartsWith("https://vavoo.to/"));
                if (doc is not null)
                {
                    await doc.Execute("window.__streamBrowserBridge.inspect(document.querySelector('video'))");
                    for (int i = 0; doc.Allowed.Count == 0 && i < 140; i++) await Task.Delay(100);
                    string? url = doc.Allowed.LastOrDefault();
                    if (url is not null)
                    {
                        await doc.Execute("window.__streamBrowserBridge.activate(" + JsonSerializer.Serialize(url) + ")");
                        for (int i = 0; window.SuccessfulHandoffs == 0 && window.LastHandoffError.Length == 0 && i < 230; i++) await Task.Delay(100);
                    }
                    File.WriteAllText(Path.Combine(output, $"vavoo-{name}-handoff.json"), JsonSerializer.Serialize(new
                    {
                        verified = doc.Allowed.Select(u => new Uri(u).GetLeftPart(UriPartial.Authority)),
                        handoffs = window.SuccessfulHandoffs,
                        error = window.LastHandoffError,
                        video = window.DiagnosticVlc.LastDecodedVideo,
                        audio = window.DiagnosticVlc.LastDecodedAudio
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                success &= window.SuccessfulHandoffs > 0;
                results.Add(new { mode = name, channel = "ZDF HD (1)", browserPlayback, handoffs = window.SuccessfulHandoffs,
                    vlcVideo = window.DiagnosticVlc.LastDecodedVideo, vlcAudio = window.DiagnosticVlc.LastDecodedAudio, error = window.LastHandoffError });
            }
            File.WriteAllText(Path.Combine(output, "vavoo-result.json"), JsonSerializer.Serialize(new { success, results }, new JsonSerializerOptions { WriteIndented = true }));
            if (!success) throw new InvalidOperationException("Vavoo browser/VLC check failed; see vavoo-result.json and per-mode network reports");
        }
        finally { foreach (var child in normal.PageWindows.ToArray()) child.Close(); }
    }
}
