using System.Text.Json;

namespace StreamBrowser.Diagnostics;

internal static class PopupChecks
{
    internal static async Task RunAsync(MainWindow main, FixtureServer server, string output)
    {
        var passed = new List<string>();
        async Task Wait(Func<bool> condition, string message, int seconds = 20)
        {
            for (int i = 0; i < seconds * 10; i++)
            {
                if (condition()) return;
                await Task.Delay(100);
            }
            var states = new List<object>();
            foreach (var child in main.PageWindows.ToArray())
                states.Add(new { child.BrowserReady, child.StartupError, source = child.BrowserReady ? child.Core.Source : "", documents = child.Documents.Count,
                    events = child.DiagnosticEvents, messaging = child.BrowserReady && child.Core.Settings.IsWebMessageEnabled,
                    state = child.BrowserReady ? await child.Core.ExecuteScriptAsync("JSON.stringify({url:location.href,bridge:!!window.__streamBrowserBridge,text:document.body?.innerText.slice(0,300)})") : "" });
            File.WriteAllText(Path.Combine(output, "popup-failure-state.json"), JsonSerializer.Serialize(new { pending = main.HasPendingPopup, states }, new JsonSerializerOptions { WriteIndented = true }));
            throw new InvalidOperationException(message);
        }
        async Task Trusted(MainWindow window, string expression) =>
            await window.Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression, userGesture = true }));
        async Task<MainWindow> NewPopup()
        {
            await Wait(() => main.PageWindows.Count == 1 && main.PageWindows[0].BrowserReady, "Popup failed to initialize");
            var child = main.PageWindows[0];
            await Wait(() => child.Documents.Count > 0, "Popup failed to load its document bridge");
            return child;
        }
        main.DiagnosticNavigate(new Uri(server.BaseUrl).Authority + "/fixture");
        await Wait(() => main.Documents.Count > 0, "Opener did not load");
        if (main.Core.Source != server.BaseUrl + "/fixture") throw new InvalidOperationException("Address without scheme did not use HTTP");
        passed.Add("Address without a scheme navigates directly over HTTP");
        string target = JsonSerializer.Serialize(server.BaseUrl + "/fixture");
        await Trusted(main, "window.__popup=window.open(" + target + ", '_blank')");
        var popup = await NewPopup();
        if (await popup.Core.ExecuteScriptAsync("!!window.opener && document.referrer===location.href") != "true")
            throw new InvalidOperationException("Popup lost opener/referrer");
        var doc = popup.Documents.Single(d => d.FrameUrl == server.BaseUrl + "/fixture");
        await doc.Execute("window.__streamBrowserBridge.inspect(document.querySelector('video'))");
        await Wait(() => doc.Allowed.Count > 0, "Popup could not verify its video source");
        await doc.Execute("window.__streamBrowserBridge.activate(null)");
        await Wait(() => popup.SuccessfulHandoffs > 0 && popup.StopAcknowledgements > 0, "Popup VLC handoff failed: " + popup.LastHandoffError, 25);
        if (popup.DiagnosticVlc.LastDecodedVideo <= 0) throw new InvalidOperationException("Popup VLC did not decode video");
        passed.Add("User click opens a managed window, preserving opener/referrer, video detection and actual VLC handoff");
        await main.Core.ExecuteScriptAsync("window.__popup.close()");
        await Wait(() => main.PageWindows.Count == 0, "window.close did not release the popup");
        passed.Add("Page-initiated window.close releases only the child window");

        await Trusted(main, "window.__blank=window.open('about:blank');setTimeout(()=>window.__blank.location=" + target + ",100)");
        popup = await NewPopup();
        if (await popup.Core.ExecuteScriptAsync("!!window.opener") != "true") throw new InvalidOperationException("Deferred blank popup lost opener");
        popup.Close();
        passed.Add("Opening about:blank before assigning the target URL is supported");

        main.DiagnosticNavigate(server.BaseUrl + "/popup-auto");
        await Wait(() => main.HasPendingPopup, "Automatic popup was not offered for manual opening");
        if (main.PageWindows.Count != 0) throw new InvalidOperationException("Automatic popup opened without a click");
        main.DiagnosticOpenPopup();
        popup = await NewPopup();
        popup.Close();
        passed.Add("Automatic HTTP/HTTPS popup is blocked and can be opened with the native button");

        await Trusted(main, "window.open(" + JsonSerializer.Serialize(server.BaseUrl + "/popup-redirect") + ")");
        popup = await NewPopup();
        string expected = server.BaseUrl.Replace("127.0.0.1", "localhost") + "/fixture";
        if (popup.Core.Source != expected) throw new InvalidOperationException("Initial popup redirect was blocked: " + popup.Core.Source);
        if (await popup.Core.ExecuteScriptAsync("!!window.opener") != "true") throw new InvalidOperationException("Popup lost opener after redirect");
        await popup.Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = "location.href=" + target, userGesture = false }));
        await Wait(() => popup.Core.Source == server.BaseUrl + "/fixture", "Popup could not navigate to another HTTP origin");
        popup.Close();
        passed.Add("Popup follows cross-origin redirects and can continue navigating to other HTTP origins");
        await Trusted(main, "window.open(" + JsonSerializer.Serialize(server.BaseUrl + "/popup-script-redirect") + ")");
        popup = await NewPopup();
        await Wait(() => popup.Core.Source == expected, "Empty popup redirect page could not hand off with JavaScript");
        popup.Close();
        passed.Add("Empty redirect document can hand off with JavaScript after NavigationCompleted");

        string report = JsonSerializer.Serialize(new { success = true, passed }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "popup-result.json"), report);
        File.WriteAllText(Path.Combine(output, "result.json"), report);
    }
}
