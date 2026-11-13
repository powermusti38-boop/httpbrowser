using System.Text.Json;
using StreamBrowser.Core;

namespace StreamBrowser.Browser;

internal sealed class BrowserSession(string id, string frameUrl, int frameKey, Action<string> send, Func<string, Task<string>> execute)
{
    public string Id { get; } = id;
    public string FrameUrl { get; } = frameUrl;
    public int FrameKey { get; } = frameKey;
    public Action<string> Send { get; } = send;
    public Func<string, Task<string>> Execute { get; } = execute;
    public Dictionary<string, string> Sources { get; } = new(StringComparer.Ordinal);
    public string VideoId { get; set; } = "";
    public string CurrentSource { get; set; } = "";
    public bool Encrypted { get; set; }
    public int Revision { get; set; }
    public string Token { get; set; } = "";
    public HashSet<string> Allowed { get; } = new(StringComparer.Ordinal);
    public void Post(object message) => Send(JsonSerializer.Serialize(message));
}

internal sealed record BlockRule(string FrameUrl, HashSet<string> Urls, string[] Prefixes)
{
    public bool Matches(string url, string referer) => Urls.Contains(url) ||
        (referer == FrameUrl && Prefixes.Any(p => url.StartsWith(p, StringComparison.Ordinal)));
    public static BlockRule Create(string frameUrl, StreamSource source, ProbeResult probe)
    {
        var urls = probe.ResourceUrls.Append(source.Url).ToHashSet(StringComparer.Ordinal);
        var prefixes = probe.ResourceUrls.Where(s => Uri.TryCreate(s, UriKind.Absolute, out var u) &&
                (u.AbsolutePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || u.AbsolutePath.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase)))
            .Select(s => new Uri(new Uri(s), ".")).Where(u => u.AbsolutePath != "/").Select(u => u.AbsoluteUri).Distinct().ToArray();
        return new(frameUrl, urls, prefixes);
    }
}
