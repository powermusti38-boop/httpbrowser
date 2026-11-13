using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StreamBrowser.Core;

namespace StreamBrowser;

internal sealed class StartSitesWindow : Window
{
    private readonly Settings settings;
    private readonly StackPanel sites = new();
    private readonly TextBlock error = new() { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap };
    internal string? SelectedUrl { get; private set; }
    private static readonly string[] Colors = ["#E54862", "#7755D9", "#078A95", "#C46613", "#2867C7"];

    internal StartSitesWindow(Settings settings)
    {
        this.settings = settings;
        Title = "StreamBrowser · Startseite auswählen";
        Width = 620; Height = 590; MinWidth = 420; MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(16, 24, 32)); Foreground = Brushes.White;
        var layout = new DockPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = "Wo möchtest du starten?", FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var bottom = new StackPanel();
        bottom.Children.Add(error);
        var add = new Button { Content = "+  Adresse hinzufügen", FontSize = 20, Padding = new Thickness(14), Margin = new Thickness(0, 12, 0, 0) };
        add.Click += (_, _) => AddSite(); bottom.Children.Add(add);
        DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        layout.Children.Add(new ScrollViewer { Content = sites, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout; RenderSites();
    }

    private void RenderSites()
    {
        sites.Children.Clear();
        for (int i = 0; i < settings.StartUrls.Count; i++)
        {
            string url = settings.StartUrls[i]; int index = i;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var remove = new Button { Content = "−", ToolTip = "Diese Adresse löschen", Width = 52, FontSize = 28, Margin = new Thickness(10, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                settings.StartUrls.RemoveAt(index);
                if (!Save()) settings.StartUrls.Insert(index, url);
                RenderSites();
            };
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var open = new Button { Content = WebAddress.TryHttp(url, out var uri) ? uri.Host : url,
                ToolTip = url, FontSize = 27, FontWeight = FontWeights.Bold, Padding = new Thickness(18),
                Background = (Brush)new BrushConverter().ConvertFromString(Colors[i % Colors.Length])!, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => { SelectedUrl = url; DialogResult = true; };
            row.Children.Add(open); sites.Children.Add(row);
        }
    }

    private bool Save()
    {
        try { settings.Save(); error.Text = ""; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error.Text = "Speichern fehlgeschlagen: " + ex.Message; return false; }
    }

    private void AddSite()
    {
        var entry = new Window { Owner = this, Title = "Adresse hinzufügen", Width = 470, Height = 200,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Webadresse (z. B. example.com)", Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { FontSize = 18 }; panel.Children.Add(input);
        var message = new TextBlock { Foreground = Brushes.Firebrick }; panel.Children.Add(message);
        var accept = new Button { Content = "Hinzufügen", IsDefault = true, Padding = new Thickness(10), Margin = new Thickness(0, 12, 0, 0) };
        accept.Click += (_, _) =>
        {
            string text = input.Text.Trim(); if (!text.Contains("://")) text = "http://" + text;
            if (!WebAddress.TryHttp(text, out var uri)) { message.Text = "Bitte eine gültige HTTP-/HTTPS-Adresse eingeben."; return; }
            if (settings.StartUrls.Contains(uri.AbsoluteUri)) { message.Text = "Diese Adresse ist bereits vorhanden."; return; }
            settings.StartUrls.Add(uri.AbsoluteUri);
            if (!Save()) { settings.StartUrls.RemoveAt(settings.StartUrls.Count - 1); return; }
            entry.DialogResult = true; RenderSites();
        };
        panel.Children.Add(accept); entry.Content = panel; entry.Loaded += (_, _) => input.Focus(); entry.ShowDialog();
    }
}
