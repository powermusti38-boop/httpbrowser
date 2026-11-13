using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace StreamBrowser.Diagnostics;

internal static class ItemPopupChecks
{
    internal const string PageUrl = "https://vavoo.to/#/item/tmdb/video/623";
    internal static async Task RunAsync(MainWindow window, string output)
    {
        var popups = new List<object>();
        try
        {
            for (int i = 0; !window.BrowserReady && i < 150; i++) await Task.Delay(100);
            if (!window.BrowserReady) throw new InvalidOperationException(window.StartupError);
            window.Core.NewWindowRequested += (_, args) => popups.Add(new { url = args.Uri, userInitiated = args.IsUserInitiated });
            window.DiagnosticNavigate(PageUrl);
            await Task.Delay(13000);
            await Snapshot("initial");
            await window.Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = "document.querySelector('.btn-hero-play').click()", userGesture = true }));
            await Task.Delay(8000);
            await Snapshot("sources");
            bool alternate = Environment.GetCommandLineArgs().Contains("--item-alternate");
            string select = alternate ? "[...document.querySelectorAll('.source-item')].find(e=>e.textContent.includes('voe.sx')).click()" : "document.querySelector('.source-item').click()";
            await window.Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = select, userGesture = true }));
            await Task.Delay(10000);
            await Snapshot("selected");
            var children = new List<object>();
            foreach (var child in window.PageWindows.ToArray())
            {
                if (!child.BrowserReady) throw new InvalidOperationException("Popup did not initialize: " + child.StartupError);
                string state = JsonSerializer.Deserialize<string>(await child.Core.ExecuteScriptAsync("JSON.stringify({url:location.href,title:document.title,text:document.body?.innerText.slice(0,3000),bridge:!!window.__streamBrowserBridge,videos:document.querySelectorAll('video').length})"))!;
                children.Add(new { state = JsonSerializer.Deserialize<JsonElement>(state), documents = child.Documents.Count });
                await using var image = File.Create(Path.Combine(output, $"item-popup-{children.Count}.png"));
                await child.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            }
            string report = JsonSerializer.Serialize(new { success = children.Count > 0, testedSource = alternate ? "voe.sx" : "dood.to", requested = popups, children }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(output, "item-popup-result.json"), report);
            File.WriteAllText(Path.Combine(output, "result.json"), report);
            if (children.Count == 0) throw new InvalidOperationException("Source click did not open a StreamBrowser window");
        }
        finally { foreach (var child in window.PageWindows.ToArray()) child.Close(); }
        async Task Snapshot(string step)
        {
            string json = await window.Core.ExecuteScriptAsync("JSON.stringify({url:location.href,text:document.body.innerText.slice(0,14000),elements:[...document.querySelectorAll('button,a,[role=button],input')].slice(0,150).map(e=>({tag:e.tagName,cls:e.className,text:e.textContent.slice(0,100),href:e.getAttribute('href'),title:e.getAttribute('title')}))})");
            File.WriteAllText(Path.Combine(output, $"item-{step}.json"), JsonSerializer.Deserialize<string>(json));
            File.WriteAllText(Path.Combine(output, "item-popups.json"), JsonSerializer.Serialize(popups, new JsonSerializerOptions { WriteIndented = true }));
            await using var image = File.Create(Path.Combine(output, $"item-{step}.png"));
            await window.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        }
    }
}
