using System.Collections.Concurrent;

namespace One4All.Reporting.Engine;

/// <summary>
/// Pictures taken at render time from a file path or an http(s) URL (brief §13.1 "images from URL or path"): read once per source per
/// process and kept as a data URI, so a logo on a share or a product picture on a web server is printed without being pasted into the
/// layout. A source that cannot be read renders as no picture.
/// </summary>
public static class ImageSources
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Resolves a source to a data URI, or null when it cannot be read.</summary>
    /// <param name="source">A file path or an http(s) URL.</param>
    /// <returns>The data URI, or null.</returns>
    public static string? Resolve(string source)
    {
        string key = (source ?? string.Empty).Trim();
        return key.Length == 0 ? null : Cache.GetOrAdd(key, Load);
    }

    /// <summary>Forgets what was read, so a changed file or page is read again.</summary>
    public static void Forget() => Cache.Clear();

    private static string? Load(string source)
    {
        try
        {
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using HttpResponseMessage response = Http.GetAsync(new Uri(source)).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                string type = response.Content.Headers.ContentType?.MediaType is string media && media.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? media : MediaType(source);
                return bytes.Length == 0 ? null : $"data:{type};base64,{Convert.ToBase64String(bytes)}";
            }

            if (!File.Exists(source))
            {
                return null;
            }

            byte[] file = File.ReadAllBytes(source);
            return file.Length == 0 ? null : $"data:{MediaType(source)};base64,{Convert.ToBase64String(file)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or UriFormatException or NotSupportedException)
        {
            return null;
        }
    }

    private static string MediaType(string source) => Path.GetExtension(source).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        _ => "image/png",
    };
}
