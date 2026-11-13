using System.IO;
using System.Text.Json;
using System.Windows;
using StreamBrowser.Browser;
using StreamBrowser.Core;
using Microsoft.Web.WebView2.Core;

namespace StreamBrowser;

public partial class MainWindow
{
    private async void Receive(CoreWebView2WebMessageReceivedEventArgs args, int frameKey, Action<string> send, Func<string, Task<string>> execute)
    {
        try
        {
            if (args.WebMessageAsJson.Length > 32768 || !WebAddress.TryHttp(args.Source, out _)) return;
            using var json = JsonDocument.Parse(args.WebMessageAsJson);
            var m = json.RootElement; var type = Text(m, "type"); var id = Text(m, "doc");
            if (id.Length is < 8 or > 80 || !SameDocument(args.Source, Text(m, "frameUrl"))) return;
            if (!documents.TryGetValue(id, out var document))
            {
                if (type != "hello" || documents.Count >= 128) return;
                document = new(id, args.Source, frameKey, send, execute); documents[id] = document;
                foreach (var item in observed.Where(o => SameDocument(o.Referer, document.FrameUrl))) AddSource(document, item.Url, item.Type);
            }
            if (document.FrameKey != frameKey || !SameDocument(document.FrameUrl, args.Source)) return;
            if (vlc.DiagnosticMode && type is "hover" or "handoff" or "stopped") DiagnosticEvents.Add(type + ":" + frameKey + ":" + Text(m,"id"));
            switch (type)
            {
                case "network": AddSource(document, Text(m, "url"), Text(m, "contentType")); break;
                case "hover":
                    document.VideoId = Text(m, "id"); document.CurrentSource = Text(m, "src");
                    document.Encrypted = m.TryGetProperty("encrypted", out var encrypted) && encrypted.ValueKind == JsonValueKind.True;
                    await CheckVideoAsync(document); break;
                case "handoff":
                    if (document.Token.Length == 0 || Text(m, "token") != document.Token || Text(m, "id") != document.VideoId || !document.Allowed.Contains(Text(m, "url"))) return;
                    await HandoffAsync(document, Text(m, "url"), Number(m, "currentTime")); break;
                case "stopped": StopAcknowledgements++; break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        { if (!closing) SetStatus("Videoinformation konnte nicht verarbeitet werden: " + e.Message); }
    }
    private void AddSource(BrowserSession document, string url, string type)
    {
        if (!IsSource(url, type) || document.Sources.Count >= 60 || url.Length > 12000) return;
        document.Sources[url] = type;
    }
    private StreamSource Source(BrowserSession doc, string url) => new(url, doc.FrameUrl, Browser.CoreWebView2.Settings.UserAgent);
    private bool Current(BrowserSession doc) => !closing && documents.TryGetValue(doc.Id, out var current) && ReferenceEquals(current, doc);
    private async Task CheckVideoAsync(BrowserSession doc)
    {
        if (handingOff || !Current(doc)) return;
        int revision = ++doc.Revision; string video = doc.VideoId;
        doc.Token = ""; doc.Allowed.Clear();
        if (doc.Encrypted)
        {
            doc.Post(new { type = "result", doc = doc.Id, id = video, message = "Geschützter Player / DRM wird nicht unterstützt.", candidates = Array.Empty<object>() }); return;
        }
        var candidates = WebAddress.TryHttp(doc.CurrentSource, out _) ? new[] { doc.CurrentSource }
            : doc.Sources.Where(p => p.Value.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || new Uri(p.Key).AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).TakeLast(8).ToArray();
        if (candidates.Length == 0)
        {
            doc.Post(new { type = "result", doc = doc.Id, id = video, message = "Noch keine übertragbare Quelle. Video starten und erneut darüberfahren.", candidates = Array.Empty<object>() }); return;
        }
        SetStatus("Stream wird geprüft …"); var token = navigation.Token;
        var results = await Task.WhenAll(candidates.Select(async url =>
        {
            var source = Source(doc, url); string key = doc.FrameUrl + "\n" + url;
            if (cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromSeconds(20)) return (url, cached.Result);
            var result = await probe.CheckAsync(source, token); cache[key] = (DateTime.UtcNow, result); return (url, result);
        }));
        if (!Current(doc) || revision != doc.Revision) return;
        var ready = results.Where(p => p.Item2.Success).ToArray();
        foreach (var item in ready) doc.Allowed.Add(item.url);
        doc.Token = Guid.NewGuid().ToString("N");
        doc.Post(new { type = "result", doc = doc.Id, id = video, token = doc.Token,
            message = ready.Length > 1 ? "Mehrere Quellen · bitte auswählen" : ready.Length == 1 ? "Quelle geprüft" : results[0].Item2.Message,
            candidates = ready.Select((p, i) => new { url = p.url, label = $"{i + 1} · {p.Item2.Kind.ToUpperInvariant()} · {new Uri(p.url).Host} · {Path.GetFileName(new Uri(p.url).AbsolutePath)}" }).ToArray() });
        SetStatus(ready.Length == 0 ? results[0].Item2.Message : "Quelle geprüft. „In VLC öffnen“ übergibt das Video.");
    }
    private async Task HandoffAsync(BrowserSession doc, string url, double seconds)
    {
        if (handingOff || !Current(doc)) return;
        handingOff = true; string video = doc.VideoId; doc.Token = ""; LastHandoffError = ""; var ct = navigation.Token;
        try
        {
            var executable = VlcLauncher.FindInstalled(settings.VlcPath);
            if (executable is null) { ChooseVlcClick(this, new RoutedEventArgs()); executable = VlcLauncher.FindInstalled(settings.VlcPath); }
            if (executable is null) { Fail("VLC fehlt. Bitte VLC wählen."); return; }
            SetStatus("Quelle erneut prüfen und VLC starten …");
            var source = Source(doc, url); var result = await probe.CheckAsync(source, ct);
            if (!result.Success) { Fail(result.Message); return; }
            if (!Current(doc)) return;
            var launched = await vlc.PlayAsync(executable, source, seconds, result.IsLive, ct);
            if (!launched.Success) { Fail(launched.Message); return; }
            if (!Current(doc)) return;
            var block = BlockRule.Create(doc.FrameUrl, source, result); blocks.Add(block);
            doc.Post(new { type = "stop", doc = doc.Id, id = video, urls = block.Urls.ToArray(), prefixes = block.Prefixes });
            SuccessfulHandoffs++; ReturnButton.Visibility = Visibility.Visible; SetStatus(launched.Message);
            DiagnosticEvents.Add("handoff:" + result.Kind);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { Fail("Übergabe fehlgeschlagen: " + e.Message); }
        finally { handingOff = false; }
        void Fail(string message)
        {
            LastHandoffError = message; SetStatus(message);
            if (Current(doc)) doc.Post(new { type = "result", doc = doc.Id, id = video, message, candidates = Array.Empty<object>() });
        }
    }
}
