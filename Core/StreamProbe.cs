using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace StreamBrowser.Core;

/// <summary>Bounded, cookie-free probes; never download an entire movie to inspect it.</summary>
public sealed partial class StreamProbe : IDisposable
{
    private readonly HttpClient client = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = true,
        MaxAutomaticRedirections = 6, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(10) };
    public async Task<ProbeResult> CheckAsync(StreamSource source, CancellationToken cancellationToken = default)
    {
        if (!WebAddress.TryHttp(source.Url, out var uri)) return new(false, "Keine direkte HTTP-/HTTPS-Quelle.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            return await InspectAsync(uri, source, new HashSet<string>(StringComparer.Ordinal), 0, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, "Streamprüfung: Zeitüberschreitung."); }
        catch (HttpRequestException e) { return new(false, "Quelle nicht erreichbar: " + e.HttpRequestError); }
        catch (IOException) { return new(false, "Quelle konnte nicht gelesen werden."); }
        catch (InvalidDataException e) { return new(false, e.Message); }
    }
    private async Task<ProbeResult> InspectAsync(Uri uri, StreamSource source, HashSet<string> resources, int depth, CancellationToken ct)
    {
        if (depth > 4) return new(false, "Zu viele verschachtelte HLS-Manifeste.");
        var fetched = await FetchAsync(uri, source, 256 * 1024, ct);
        resources.Add(uri.AbsoluteUri); resources.Add(fetched.Url.AbsoluteUri);
        if (!fetched.Ok) return new(false, fetched.Error);
        var text = Encoding.UTF8.GetString(fetched.Bytes).TrimStart('\uFEFF', '\r', '\n', ' ', '\t');
        if (text.StartsWith("#EXTM3U", StringComparison.Ordinal))
        {
            var lines = text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (lines.Any(s => (s.StartsWith("#EXT-X-SESSION-KEY:") || s.StartsWith("#EXT-X-KEY:")) && IsProtectedKey(s)))
                return new(false, "Geschützter HLS-Stream / DRM wird nicht unterstützt.");
            for (int i = 0; i + 1 < lines.Length; i++)
            {
                if (!lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)) continue;
                var result = await InspectAsync(Resolve(fetched.Url, lines[i + 1]), source, resources, depth + 1, ct);
                return result with { Resources = resources.ToArray() };
            }
            var segment = lines.FirstOrDefault(s => !s.StartsWith('#'));
            if (segment is null) return new(false, "HLS enthält noch kein prüfbares Mediensegment.");
            foreach (var line in lines.Where(s => s.StartsWith("#EXT-X-KEY:") || s.StartsWith("#EXT-X-MAP:")))
            {
                var match = AttributeUri().Match(line);
                if (!match.Success) continue;
                var auxiliary = Resolve(fetched.Url, match.Groups[1].Value);
                resources.Add(auxiliary.AbsoluteUri);
                var test = await FetchAsync(auxiliary, source, 4096, ct);
                if (!test.Ok || test.Bytes.Length == 0) return new(false, "HLS-Schlüssel oder Initialsegment nicht erreichbar.");
            }
            var segmentUrl = Resolve(fetched.Url, segment); resources.Add(segmentUrl.AbsoluteUri);
            var sample = await FetchAsync(segmentUrl, source, 4096, ct);
            if (!sample.Ok || sample.Bytes.Length == 0 || sample.Type.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                return new(false, "HLS-Mediensegment nicht verfügbar.");
            return new(true, "HLS geprüft", "hls", !lines.Contains("#EXT-X-ENDLIST"), resources.ToArray());
        }
        if (fetched.Bytes.Length >= 12 && Encoding.ASCII.GetString(fetched.Bytes, 4, 4) is "ftyp" or "styp")
            return new(true, "MP4 geprüft", "mp4", false, resources.ToArray());
        return new(false, "Keine unterstützte MP4-/HLS-Quelle; möglicherweise Anmeldung oder Sitzung erforderlich.");
    }
    private static bool IsProtectedKey(string line) =>
        (line.Contains("METHOD=") && !line.Contains("METHOD=NONE") && !line.Contains("METHOD=AES-128")) ||
        (line.Contains("KEYFORMAT=") && !line.Contains("KEYFORMAT=\"identity\""));
    private static Uri Resolve(Uri basis, string relative)
    {
        if (!Uri.TryCreate(basis, relative, out var uri) || !WebAddress.TryHttp(uri.AbsoluteUri, out _))
            throw new InvalidDataException("Ungültige Medienadresse im Manifest.");
        return uri;
    }
    private async Task<(bool Ok, byte[] Bytes, string Type, Uri Url, string Error)> FetchAsync(Uri uri, StreamSource source, int limit, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(0, limit - 1);
        if (WebAddress.TryHttp(source.Referer, out var referer)) request.Headers.Referrer = referer;
        if (source.UserAgent.Length > 0 && !source.UserAgent.Any(char.IsControl)) request.Headers.TryAddWithoutValidation("User-Agent", source.UserAgent);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var actual = response.RequestMessage!.RequestUri!;
        if (!WebAddress.TryHttp(actual.AbsoluteUri, out _)) throw new InvalidDataException("Nicht unterstützte Weiterleitung.");
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "Quelle verweigert Zugriff; Anmeldung, Cookies oder weitere Zugangsdaten erforderlich."
                : $"Quelle antwortet mit HTTP {(int)response.StatusCode}.";
            return (false, [], "", actual, message);
        }
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var block = new byte[8192];
        while (buffer.Length < limit)
        {
            int n = await stream.ReadAsync(block.AsMemory(0, Math.Min(block.Length, limit - (int)buffer.Length)), ct);
            if (n == 0) break; buffer.Write(block, 0, n);
        }
        return (true, buffer.ToArray(), response.Content.Headers.ContentType?.MediaType ?? "", actual, "");
    }
    [GeneratedRegex("URI=\"([^\"]+)\"")]
    private static partial Regex AttributeUri();
    public void Dispose() => client.Dispose();
}
