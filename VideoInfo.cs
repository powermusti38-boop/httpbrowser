using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StreamBrowser.Core;

namespace StreamBrowser;

public partial class MainWindow
{
    private sealed record VideoState(string Name, double Current, double? Duration, double Ahead, bool Playing, double Area, DateTime At);
    private readonly Dictionary<int, VideoState> videoStates = [];
    private readonly DispatcherTimer videoTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private readonly TextBlock videoName = new() { Foreground = Brushes.LimeGreen, FontSize = 17, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock videoTime = new() { Foreground = Brushes.Gold, FontSize = 16 };
    private readonly TextBlock videoBuffer = new() { Foreground = new SolidColorBrush(Color.FromRgb(193, 133, 255)), FontSize = 16 };
    private Popup? videoPopup;
    private int videoFrameSequence;

    private async Task InitializeVideoInfoAsync(CoreWebView2 core)
    {
        var lines = new StackPanel(); lines.Children.Add(videoName); lines.Children.Add(videoTime); lines.Children.Add(videoBuffer);
        videoPopup = new Popup { PlacementTarget = Browser, Placement = PlacementMode.RelativePoint, AllowsTransparency = true,
            StaysOpen = true, IsHitTestVisible = false, Width = 420,
            Child = new Border { Background = new SolidColorBrush(Color.FromArgb(215, 12, 18, 26)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = lines } };
        videoTimer.Tick += (_, _) => UpdateVideoInfo(); videoTimer.Start();
        core.WebMessageReceived += (_, args) => ReceiveVideoInfo(0, args);
        core.FrameCreated += (_, args) => AttachVideoFrame(args.Frame);
        core.NavigationStarting += (_, _) => { videoStates.Clear(); if (videoPopup is not null) videoPopup.IsOpen = false; };
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("StreamBrowser.Browser.video-info.js")!;
        string script = await new StreamReader(resource).ReadToEndAsync();
        await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        if (popupRequest is not null)
            core.DOMContentLoaded += async (_, _) => { if (!closing) try { await core.ExecuteScriptAsync(script); } catch (InvalidOperationException) { } catch (System.Runtime.InteropServices.COMException) { } };
    }
    private void AttachVideoFrame(CoreWebView2Frame frame)
    {
        int key = ++videoFrameSequence;
        frame.WebMessageReceived += (_, args) => ReceiveVideoInfo(key, args);
        frame.FrameCreated += (_, args) => AttachVideoFrame(args.Frame);
        frame.NavigationStarting += (_, _) => videoStates.Remove(key);
        frame.Destroyed += (_, _) => videoStates.Remove(key);
    }
    private void ReceiveVideoInfo(int key, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!WebAddress.TryHttp(args.Source, out _) || args.WebMessageAsJson.Length > 4096) return;
        try
        {
            using var json = JsonDocument.Parse(args.WebMessageAsJson); var m=json.RootElement;
            if (!m.TryGetProperty("type", out var type) || type.GetString() != "videoInfo") return;
            if (!m.GetProperty("visible").GetBoolean()) { videoStates.Remove(key); return; }
            double Read(string name) => m.TryGetProperty(name,out var v) && v.TryGetDouble(out var d) && double.IsFinite(d) ? Math.Max(0,d) : 0;
            videoStates[key] = new(m.GetProperty("name").GetString() ?? "Stream", Read("current"),
                m.GetProperty("duration").ValueKind == JsonValueKind.Number ? Read("duration") : null,
                Read("ahead"), m.GetProperty("playing").GetBoolean(), Read("area"), DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { }
    }
    private void UpdateVideoInfo()
    {
        if (videoPopup is null) return;
        var state = videoStates.Values.Where(v => DateTime.UtcNow-v.At < TimeSpan.FromSeconds(3)).OrderByDescending(v=>v.Playing).ThenByDescending(v=>v.Area).FirstOrDefault();
        videoPopup.IsOpen = fullscreen && IsActive && state is not null && !closing;
        if (state is null) return;
        string Minutes(double seconds) => (seconds/60).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
        videoName.Text = state.Name;
        videoTime.Text = $"{Minutes(state.Current)} min / " + (state.Duration is double duration ? $"{Minutes(duration)} min" : "Live");
        videoBuffer.Text = $"Puffer: {Minutes(state.Ahead)} min voraus";
        videoPopup.HorizontalOffset = Math.Max(0, Browser.ActualWidth-440);
        videoPopup.VerticalOffset = Math.Max(0, Browser.ActualHeight-118);
    }
}
