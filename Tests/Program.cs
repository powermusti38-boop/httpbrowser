using StreamBrowser.Core;
using System.Text.Json;

var settings = new Settings();
if (settings.StartUrls.Count != 3) throw new Exception("Default start sites missing");
settings.StartUrls.Add("http://example.com/");
var saved = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
if (saved.StartUrls.Count != 4) throw new Exception("Added address not preserved");
saved.StartUrls.Clear();
if (JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(saved))!.StartUrls.Count != 0) throw new Exception("Deleted addresses reappeared");
if (!WebAddress.TryHttp("http://example.com/", out _) || !WebAddress.TryHttp("https://example.com/", out _)) throw new Exception("HTTP/HTTPS validation failed");
foreach (var invalid in new[] { "file:///C:/Windows/win.ini", "javascript:alert(1)", "https://user:pass@example.com/" })
    if (WebAddress.TryHttp(invalid, out _)) throw new Exception("Unsupported address accepted");
Console.WriteLine("PASS browser settings, saved addresses and HTTP/HTTPS validation");
