using System.Text.Json;

namespace StreamBrowser.Core;

public sealed class Settings
{
    public List<string> StartUrls { get; set; } = ["https://taraftarium.co/canli/", "http://oha.to/", "http://vavoo.to/"];
    public string HomeUrl { get; set; } = "https://taraftarium.co/";
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamBrowser");
    public static Settings Load()
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DataDirectory, "settings.json"))) ?? new();
            loaded.StartUrls ??= new Settings().StartUrls;
            return loaded;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(Path.Combine(DataDirectory, "settings.json"), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
public static class WebAddress
{
    public static bool TryHttp(string? text, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(text, UriKind.Absolute, out uri!) && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo);
    }
}
