using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using Microsoft.Web.WebView2.Core;
using StreamBrowser.Core;

namespace StreamBrowser;

public partial class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private bool closing;
    private bool fullscreen;
    private WindowState previousWindowState;
    private WindowStyle previousWindowStyle;
    private ResizeMode previousResizeMode;
    private bool previousTopmost;
    private readonly bool showStartDialog;
    private readonly CoreWebView2Environment? popupEnvironment;
    private readonly CoreWebView2NewWindowRequestedEventArgs? popupRequest;
    private readonly TaskCompletionSource<bool> initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<MainWindow> pageWindows = [];
    private string? pendingPopupUrl;
    internal bool BrowserReady { get; private set; }
    internal string StartupError { get; private set; } = "";
    public MainWindow() : this(true) { }
    private MainWindow(bool showStartDialog,
        CoreWebView2Environment? popupEnvironment = null, CoreWebView2NewWindowRequestedEventArgs? popupRequest = null, Uri? initialPage = null)
    {
        this.showStartDialog = showStartDialog;
        this.popupEnvironment = popupEnvironment;
        this.popupRequest = popupRequest;
        InitializeComponent();
        // WPF WebView2 forwards native accelerator keys into this routed event.
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.F11) { e.Handled = true; if (!e.IsRepeat) await ToggleFullscreenAsync(); }
            else if (e.Key == Key.Escape && fullscreen) { e.Handled = true; await LeaveFullscreenAsync(); }
        };
        Address.Text = popupRequest?.Uri ?? initialPage?.AbsoluteUri ?? settings.HomeUrl;
    }
    private void SetStatus(string text) { Status.Text = text; }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {

            // Use a dedicated profile so an older, still running version cannot
            // reuse its browser process without the HTTP option.
            string profile = "HttpBrowserProfile";
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
            core.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => { args.Cancel = true; SetStatus("Downloads sind in dieser Version deaktiviert."); };
            core.NewWindowRequested += NewWindowRequested;
            if (popupEnvironment is not null) core.WindowCloseRequested += (_, _) => Close();
            core.NavigationStarting += (_, args) =>
            {
                if (!WebAddress.TryHttp(args.Uri, out var navigationUri) && args.Uri != "about:blank") { args.Cancel = true; return; }

                pendingPopupUrl = null; PopupButton.Visibility = Visibility.Collapsed;
                SetStatus("Seite wird geladen …");
            };
            core.NavigationCompleted += (_, args) => SetStatus(args.IsSuccess
                    ? "Seite geladen · F11 = Vollbild · Esc = Vollbild verlassen"
                    : "Seite konnte nicht geladen werden: " + args.WebErrorStatus);
            core.SourceChanged += (_, _) => Address.Text = core.Source;
            core.DocumentTitleChanged += (_, _) => Title = "StreamBrowser · HTTP · " + core.DocumentTitle;
            if (popupRequest is not null) popupRequest.NewWindow = core;
            BrowserReady = true;
            initialized.TrySetResult(true);
            if (Environment.GetCommandLineArgs().Contains("--startup-check"))
            {
                Application.Current.Shutdown(0);
                return;
            }
            if (popupRequest is null)
            {
                if (showStartDialog) ShowStartSites();
                else Navigate(Address.Text);
            }
        }
        catch (Exception ex)
        {
            StartupError = ex.Message;
            initialized.TrySetResult(false);
            SetStatus("Start fehlgeschlagen: " + ex.Message + " · Gegebenenfalls Microsoft Edge WebView2 Runtime installieren.");
            if (Environment.GetCommandLineArgs().Contains("--startup-check")) Application.Current.Shutdown(1);
        }
    }
    private MainWindow WindowRoot => Owner is MainWindow owner ? owner.WindowRoot : this;
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
    private void ReloadClick(object sender, RoutedEventArgs e) => Browser.CoreWebView2?.Reload();
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
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        closing = true; initialized.TrySetResult(false); Browser.Dispose();
    }
    internal CoreWebView2 Core => Browser.CoreWebView2;
}

