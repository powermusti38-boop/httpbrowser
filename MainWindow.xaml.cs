using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using StreamBrowser.Browser;
using StreamBrowser.Core;

namespace StreamBrowser;

public partial class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly StreamProbe probe = new();
    private readonly VlcLauncher vlc = new();
    private readonly Dictionary<string, BrowserSession> documents = [];
    private readonly List<(string Url, string Referer, string Type)> observed = [];
    private readonly List<BlockRule> blocks = [];
    private readonly Dictionary<string, (DateTime Time, ProbeResult Result)> cache = [];
    private CancellationTokenSource navigation = new();
    private int frameSequence;
    private bool handingOff, closing;
    private bool fullscreen;
    private WindowState previousWindowState;
    private WindowStyle previousWindowStyle;
    private ResizeMode previousResizeMode;
    private bool previousTopmost;
    private readonly bool runDiagnostics;
    private readonly CoreWebView2Environment? popupEnvironment;
    private readonly CoreWebView2NewWindowRequestedEventArgs? popupRequest;
    private readonly TaskCompletionSource<bool> initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<MainWindow> pageWindows = [];
    private string? pendingPopupUrl;
    internal bool BrowserReady { get; private set; }
    internal string StartupError { get; private set; } = "";
    internal int SuccessfulHandoffs { get; private set; }
    internal int BlockedRequests { get; private set; }
    internal int StopAcknowledgements { get; private set; }
    internal string LastHandoffError { get; private set; } = "";
    internal List<string> DiagnosticEvents { get; } = [];
    public MainWindow() : this(true) { }
    private MainWindow(bool runDiagnostics,
        CoreWebView2Environment? popupEnvironment = null, CoreWebView2NewWindowRequestedEventArgs? popupRequest = null, Uri? initialPage = null)
    {
        this.runDiagnostics = runDiagnostics;
        this.popupEnvironment = popupEnvironment;
        this.popupRequest = popupRequest;
        InitializeComponent();
        // WPF WebView2 forwards native accelerator keys into this routed event.
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.F11) { e.Handled = true; if (!e.IsRepeat) await ToggleFullscreenAsync(); }
            else if (e.Key == Key.Escape && fullscreen) { e.Handled = true; await LeaveFullscreenAsync(); }
        };
        if (Environment.GetCommandLineArgs().Contains("--diagnostics"))
        {
            ShowInTaskbar = false;
            Left = -20000;
            Top = -20000;
        }
        Address.Text = popupRequest?.Uri ?? initialPage?.AbsoluteUri ?? settings.HomeUrl;
    }
    private void SetStatus(string text) { Status.Text = text; }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            bool diagnostic = Environment.GetCommandLineArgs().Contains("--diagnostics");
            vlc.DiagnosticMode = diagnostic;
            // Use a dedicated profile so an older, still running version cannot
            // reuse its browser process without the HTTP option.
            string profile = diagnostic ? "DiagnosticHttpBrowserProfile" : "HttpBrowserProfile";
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--allow-running-insecure-content"
            };
            var directory = Path.Combine(Settings.DataDirectory, profile);
            var environment = popupEnvironment ?? await CoreWebView2Environment.CreateAsync(null, directory, options);
            await Browser.EnsureCoreWebView2Async(environment);
            if (closing) return;
            var core = Browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
            // A requested window must receive its scripts before adoption, then its
            // request handlers after adoption. It must share the opener's environment.
            string bridgeScript;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StreamBrowser.Browser.bridge.js")!)
                bridgeScript = await new StreamReader(stream).ReadToEndAsync();
            await core.AddScriptToExecuteOnDocumentCreatedAsync(bridgeScript);
            if (popupRequest is not null)
                core.DOMContentLoaded += async (_, _) =>
                {
                    if (closing) return;
                    try { await core.ExecuteScriptAsync(bridgeScript); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
                };
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => { args.Cancel = true; SetStatus("Downloads sind in dieser Version deaktiviert."); };
            core.NewWindowRequested += NewWindowRequested;
            if (popupEnvironment is not null) core.WindowCloseRequested += (_, _) => Close();
            core.NavigationStarting += (_, args) =>
            {
                if (!WebAddress.TryHttp(args.Uri, out var navigationUri) && args.Uri != "about:blank") { args.Cancel = true; return; }
                navigation.Cancel(); navigation.Dispose(); navigation = new();
                documents.Clear(); observed.Clear(); blocks.Clear(); cache.Clear();
                pendingPopupUrl = null; PopupButton.Visibility = Visibility.Collapsed;
                ReturnButton.Visibility = Visibility.Collapsed; SetStatus("Seite wird geladen …");
            };
            core.NavigationCompleted += (_, args) => SetStatus(args.IsSuccess
                    ? "Maus über ein Video bewegen · Grün = Quelle geprüft · Übergabe über „In VLC öffnen“"
                    : "Seite konnte nicht geladen werden: " + args.WebErrorStatus);
            core.SourceChanged += (_, _) => Address.Text = core.Source;
            core.DocumentTitleChanged += (_, _) => Title = "StreamBrowser · HTTP · " + core.DocumentTitle;
            core.WebMessageReceived += (_, args) => Receive(args, 0, core.PostWebMessageAsJson, core.ExecuteScriptAsync);
            core.FrameCreated += (_, args) => AttachFrame(args.Frame);
            if (popupRequest is not null) popupRequest.NewWindow = core;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) =>
            {
                var referer = Header(args.Request.Headers, "Referer");
                if (!blocks.Any(b => b.Matches(args.Request.Uri, referer))) return;
                args.Response = environment.CreateWebResourceResponse(null, 403, "Stream transferred to VLC", "Cache-Control: no-store\r\n");
                BlockedRequests++;
            };
            core.WebResourceResponseReceived += (_, args) =>
            {
                var contentType = ResponseHeader(args.Response.Headers, "Content-Type");
                var url = args.Request.Uri;
                if (!IsSource(url, contentType)) return;
                var referer = Header(args.Request.Headers, "Referer");
                if (observed.Count >= 200) observed.RemoveAt(0);
                observed.Add((url, referer, contentType));
                foreach (var document in documents.Values.Where(d => SameDocument(d.FrameUrl, referer))) AddSource(document, url, contentType);
            };
            BrowserReady = true;
            initialized.TrySetResult(true);
            if (Environment.GetCommandLineArgs().Contains("--startup-check"))
            {
                Application.Current.Shutdown(0);
                return;
            }
            if (diagnostic && runDiagnostics) await DiagnosticRunner.RunAsync(this);
            else if (popupRequest is null)
            {
                if (runDiagnostics) ShowStartSites();
                else Navigate(Address.Text);
            }
        }
        catch (Exception ex)
        {
            StartupError = ex.Message;
            initialized.TrySetResult(false);
            SetStatus("Start fehlgeschlagen: " + ex.Message + " · Gegebenenfalls Microsoft Edge WebView2 Runtime installieren.");
            if (runDiagnostics && Environment.GetCommandLineArgs().Contains("--diagnostics")) { DiagnosticRunner.WriteFatal(ex); Application.Current.Shutdown(1); }
        }
    }
    private MainWindow WindowRoot => Owner is MainWindow owner ? owner.WindowRoot : this;
    internal IReadOnlyList<MainWindow> PageWindows => WindowRoot.pageWindows;
    internal bool HasPendingPopup => pendingPopupUrl is not null;
    internal void DiagnosticOpenPopup() => OpenPopupClick(this, new RoutedEventArgs());
    private MainWindow CreatePageWindow(Uri? target, CoreWebView2NewWindowRequestedEventArgs? request = null)
    {
        var root = WindowRoot;
        if (root.pageWindows.Count >= 6) throw new InvalidOperationException("Bitte zuerst ein zusätzliches Seitenfenster schließen (maximal 6).");
        var window = new MainWindow(false, Core.Environment, request, target) { Owner = this };
        root.pageWindows.Add(window);
        window.Closed += (_, _) => root.pageWindows.Remove(window);
        window.Show();
        return window;
    }
    private async void NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (closing) return;
        bool hasTarget = WebAddress.TryHttp(args.Uri, out var target);
        if (!hasTarget && args.Uri != "about:blank" && args.Uri.Length != 0)
        {
            SetStatus("Seitenfenster mit nicht unterstützter Adresse blockiert.");
            return;
        }
        if (!args.IsUserInitiated)
        {
            if (hasTarget)
            {
                pendingPopupUrl = target.AbsoluteUri;
                PopupButton.Content = "Seitenfenster öffnen · " + target.Host;
                PopupButton.Visibility = Visibility.Visible;
            }
            SetStatus("Automatisches Seitenfenster blockiert." + (hasTarget ? " Bei Bedarf unten „Seitenfenster öffnen“ anklicken." : ""));
            return;
        }
        MainWindow? window = null;
        try
        {
            using var deferral = args.GetDeferral();
            window = CreatePageWindow(hasTarget ? target : null, args);
            if (!await window.initialized.Task || closing)
            {
                window.Close();
                if (!closing) SetStatus("Seitenfenster konnte nicht gestartet werden: " + window.StartupError);
                return;
            }
            SetStatus("Quelle im zusätzlichen StreamBrowser-Fenster geöffnet.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            if (window?.IsLoaded == true) window.Close();
            if (!closing) SetStatus("Seitenfenster konnte nicht geöffnet werden: " + ex.Message);
        }
    }
    private void OpenPopupClick(object sender, RoutedEventArgs e)
    {
        if (!WebAddress.TryHttp(pendingPopupUrl, out var uri)) return;
        try
        {
            CreatePageWindow(uri);
            pendingPopupUrl = null; PopupButton.Visibility = Visibility.Collapsed;
            SetStatus("Seitenfenster geöffnet.");
        }
        catch (InvalidOperationException ex) { SetStatus(ex.Message); }
    }
    private void AttachFrame(CoreWebView2Frame frame)
    {
        int key = ++frameSequence;
        frame.WebMessageReceived += (_, args) => Receive(args, key, frame.PostWebMessageAsJson, frame.ExecuteScriptAsync);
        frame.FrameCreated += (_, args) => AttachFrame(args.Frame);
        frame.NavigationStarting += (_, _) => Remove();
        frame.Destroyed += (_, _) => Remove();
        void Remove() { foreach (var id in documents.Values.Where(d => d.FrameKey == key).Select(d => d.Id).ToArray()) documents.Remove(id); }
    }
    private static string Header(CoreWebView2HttpRequestHeaders headers, string name) => headers.Contains(name) ? headers.GetHeader(name) : "";
    private static string ResponseHeader(CoreWebView2HttpResponseHeaders headers, string name) => headers.Contains(name) ? headers.GetHeader(name) : "";
    private static bool SameDocument(string a, string b) => Uri.TryCreate(a, UriKind.Absolute, out var x) && Uri.TryCreate(b, UriKind.Absolute, out var y)
        && x.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped) == y.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
    private static bool IsSource(string url, string type) => WebAddress.TryHttp(url, out var uri) &&
        (type.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || type.Contains("video/mp4", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
    private static string Text(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static double Number(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : 0;
    private void Navigate(string text)
    {
        if (!text.Contains("://")) text = "http://" + text;
        if (!WebAddress.TryHttp(text, out var uri)) { SetStatus("Bitte eine HTTP-/HTTPS-Webadresse eingeben."); return; }
        Browser.CoreWebView2?.Navigate(uri.AbsoluteUri);
    }
    private void NavigateClick(object sender, RoutedEventArgs e) => Navigate(Address.Text.Trim());
    private void AddressKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(Address.Text.Trim()); }
    private void BackClick(object sender, RoutedEventArgs e) { if (Browser.CanGoBack) Browser.GoBack(); }
    private void ForwardClick(object sender, RoutedEventArgs e) { if (Browser.CanGoForward) Browser.GoForward(); }
    private void ReloadClick(object sender, RoutedEventArgs e) { blocks.Clear(); Browser.CoreWebView2?.Reload(); }
    private void StartSitesClick(object sender, RoutedEventArgs e) => ShowStartSites();
    private void ShowStartSites()
    {
        var dialog = new StartSitesWindow(settings) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedUrl is not null) Navigate(dialog.SelectedUrl);
    }
    private async Task ToggleFullscreenAsync()
    {
        if (fullscreen) await LeaveFullscreenAsync();
        else SetFullscreen(true);
    }
    private async Task LeaveFullscreenAsync()
    {
        SetFullscreen(false);
        if (Browser.CoreWebView2 is not null)
            await Browser.CoreWebView2.ExecuteScriptAsync("if(document.fullscreenElement)document.exitFullscreen().catch(()=>{});");
    }
    private void SetFullscreen(bool enabled)
    {
        if (fullscreen == enabled) return;
        fullscreen = enabled;
        if (enabled)
        {
            previousWindowState = WindowState; previousWindowStyle = WindowStyle;
            previousResizeMode = ResizeMode; previousTopmost = Topmost;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize; Topmost = true; WindowState = WindowState.Maximized;
            NavigationBar.Visibility = StatusBar.Visibility = Visibility.Collapsed;
        }
        else
        {
            WindowState = WindowState.Normal; WindowStyle = previousWindowStyle;
            ResizeMode = previousResizeMode; Topmost = previousTopmost; WindowState = previousWindowState;
            NavigationBar.Visibility = StatusBar.Visibility = Visibility.Visible;
        }
    }
    private void ChooseVlcClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "VLC auswählen (vlc.exe)", Filter = "VLC media player|vlc.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        settings.VlcPath = dialog.FileName;
        try { settings.Save(); SetStatus("VLC-Pfad gespeichert."); } catch (IOException ex) { SetStatus(ex.Message); }
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        closing = true; initialized.TrySetResult(false); navigation.Cancel(); Browser.Dispose(); probe.Dispose(); vlc.StopDiagnosticSessions(); vlc.Dispose(); navigation.Dispose();
    }
    internal CoreWebView2 Core => Browser.CoreWebView2;
    internal IReadOnlyCollection<BrowserSession> Documents => documents.Values;
    internal void DiagnosticNavigate(string url) => Navigate(url);
    internal Task DiagnosticCheck(BrowserSession session) => CheckVideoAsync(session);
    internal VlcLauncher DiagnosticVlc => vlc;
}

