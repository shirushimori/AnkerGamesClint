using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnkerGamesClient.Services;

/// <summary>
/// Scrapes a game page for its cover image and saves it locally.
/// Handles AnkerGames Next.js pages specifically, with broad fallbacks.
/// </summary>
public partial class BannerScraperService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static BannerScraperService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        Http.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        Http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    /// <summary>
    /// Scrapes <paramref name="pageUrl"/> for a cover image and saves it under
    /// <paramref name="saveDir"/>/assets/game.{ext}.
    /// Returns the saved path, or null on failure.
    /// </summary>
    public async Task<string?> ScrapeAndSaveAsync(string pageUrl, string saveDir)
    {
        try
        {
            var html = await FetchHtmlAsync(pageUrl);
            if (string.IsNullOrWhiteSpace(html))
            {
                System.Diagnostics.Debug.WriteLine($"[Banner] No HTML from {pageUrl}");
                return null;
            }

            var imageUrl = FindBestImageUrl(html, pageUrl);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                System.Diagnostics.Debug.WriteLine($"[Banner] No image URL found in {pageUrl}");
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[Banner] Found image: {imageUrl}");
            return await DownloadImageAsync(imageUrl, saveDir);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Banner] Error: {ex.Message}");
            return null;
        }
    }

    // ── HTML fetch ───────────────────────────────────────────────────────────

    private static async Task<string?> FetchHtmlAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // Mimic a real browser request so SSR sites return full content
            req.Headers.Add("Cache-Control", "no-cache");
            req.Headers.Add("Pragma", "no-cache");

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead);
            if (!resp.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Banner] HTTP {(int)resp.StatusCode} for {url}");
                return null;
            }
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Banner] Fetch error: {ex.Message}");
            return null;
        }
    }

    // ── Image URL discovery ──────────────────────────────────────────────────

    private static string? FindBestImageUrl(string html, string pageUrl)
    {
        // ── 1. Parse __NEXT_DATA__ JSON (AnkerGames is Next.js) ──────────────
        var nextDataUrl = ExtractFromNextData(html, pageUrl);
        if (nextDataUrl is not null) return nextDataUrl;

        // ── 2. og:image — most reliable meta tag ─────────────────────────────
        // Try both attribute orderings
        foreach (var rx in new[] { OgImagePropFirst(), OgImageContentFirst() })
        {
            var m = rx.Match(html);
            if (m.Success)
            {
                var u = ResolveUrl(m.Groups[1].Value, pageUrl);
                if (u is not null) return u;
            }
        }

        // ── 3. twitter:image ─────────────────────────────────────────────────
        {
            var m = TwitterImage().Match(html);
            if (m.Success)
            {
                var u = ResolveUrl(m.Groups[1].Value, pageUrl);
                if (u is not null) return u;
            }
        }

        // ── 4. <picture><source srcset="..."> — Next.js Image component ──────
        foreach (Match m in PictureSrcset().Matches(html))
        {
            var best = BestFromSrcset(m.Groups[1].Value);
            if (best is not null) return ResolveUrl(best, pageUrl);
        }

        // ── 5. <img srcset="..."> ─────────────────────────────────────────────
        foreach (Match m in ImgSrcset().Matches(html))
        {
            var best = BestFromSrcset(m.Groups[1].Value);
            if (best is not null && !best.StartsWith("data:"))
                return ResolveUrl(best, pageUrl);
        }

        // ── 6. <img class="...object-cover..."> with real src ─────────────────
        foreach (Match m in ObjectCoverSrc().Matches(html))
        {
            var src = m.Groups[1].Value;
            if (!src.StartsWith("data:") && !string.IsNullOrWhiteSpace(src))
                return ResolveUrl(src, pageUrl);
        }

        // ── 7. Any img src that looks like a cover ────────────────────────────
        foreach (Match m in AnyImgSrc().Matches(html))
        {
            var src = m.Groups[1].Value;
            if (!src.StartsWith("data:") && LooksCoverLike(src))
                return ResolveUrl(src, pageUrl);
        }

        return null;
    }

    /// <summary>
    /// Parses the Next.js <script id="__NEXT_DATA__"> JSON blob and walks the
    /// object tree looking for any string value that is an image URL.
    /// </summary>
    private static string? ExtractFromNextData(string html, string pageUrl)
    {
        var m = NextDataScript().Match(html);
        if (!m.Success) return null;

        var json = m.Groups[1].Value;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return WalkJsonForImage(doc.RootElement, pageUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Banner] __NEXT_DATA__ parse error: {ex.Message}");
            return null;
        }
    }

    private static string? WalkJsonForImage(JsonElement el, string pageUrl, int depth = 0)
    {
        if (depth > 12) return null; // prevent infinite recursion on deep objects

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                // Priority keys first
                var priorityKeys = new[]
                {
                    "coverImage", "cover_image", "cover", "image", "imageUrl",
                    "thumbnail", "poster", "banner", "headerImage", "boxArt",
                    "capsuleImage", "heroImage", "backgroundImage"
                };
                foreach (var key in priorityKeys)
                {
                    if (el.TryGetProperty(key, out var val) &&
                        val.ValueKind == JsonValueKind.String)
                    {
                        var s = val.GetString() ?? "";
                        if (IsImageUrl(s)) return ResolveUrl(s.Replace("\\u002F", "/"), pageUrl);
                    }
                }
                // Then recurse into all properties
                foreach (var prop in el.EnumerateObject())
                {
                    var result = WalkJsonForImage(prop.Value, pageUrl, depth + 1);
                    if (result is not null) return result;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    var result = WalkJsonForImage(item, pageUrl, depth + 1);
                    if (result is not null) return result;
                }
                break;

            case JsonValueKind.String:
                var str = el.GetString() ?? "";
                if (IsImageUrl(str)) return ResolveUrl(str.Replace("\\u002F", "/"), pageUrl);
                break;
        }
        return null;
    }

    private static bool IsImageUrl(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.StartsWith("data:")) return false;
        var lower = s.ToLowerInvariant();
        return (lower.Contains(".jpg") || lower.Contains(".jpeg") ||
                lower.Contains(".png") || lower.Contains(".webp") ||
                lower.Contains(".gif")) &&
               (lower.StartsWith("http") || lower.StartsWith("/"));
    }

    private static string? BestFromSrcset(string srcset)
    {
        // srcset format: "url1 1x, url2 2x" or "url1 144w, url2 288w"
        return srcset.Split(',')
                     .Select(s => s.Trim().Split(' ')[0])
                     .LastOrDefault(s => !string.IsNullOrWhiteSpace(s) &&
                                         !s.StartsWith("data:"));
    }

    private static bool LooksCoverLike(string src)
    {
        var lower = src.ToLowerInvariant();
        return lower.Contains("cover") || lower.Contains("banner") ||
               lower.Contains("poster") || lower.Contains("header") ||
               lower.Contains("capsule") || lower.Contains("box") ||
               lower.Contains("thumbnail") || lower.Contains("hero");
    }

    private static string? ResolveUrl(string src, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        src = src.Replace("&amp;", "&").Trim();
        if (src.StartsWith("data:")) return null;
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return src;
        try { return new Uri(new Uri(pageUrl), src).ToString(); }
        catch { return null; }
    }

    // ── Image download ───────────────────────────────────────────────────────

    private static async Task<string?> DownloadImageAsync(string imageUrl, string saveDir)
    {
        var assetsDir = Path.Combine(saveDir, "assets");
        Directory.CreateDirectory(assetsDir);

        var ext = GetExtFromUrl(imageUrl);
        var savePath = Path.Combine(assetsDir, $"game{ext}");

        try
        {
            using var resp = await Http.GetAsync(imageUrl, HttpCompletionOption.ResponseContentRead);
            resp.EnsureSuccessStatusCode();

            var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (ct.Contains("jpeg") || ct.Contains("jpg")) ext = ".jpg";
            else if (ct.Contains("webp"))                   ext = ".webp";
            else if (ct.Contains("gif"))                    ext = ".gif";
            else if (ct.Contains("png"))                    ext = ".png";
            savePath = Path.Combine(assetsDir, $"game{ext}");

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 512)
            {
                System.Diagnostics.Debug.WriteLine($"[Banner] Image too small ({bytes.Length}B), skipping");
                return null;
            }

            await File.WriteAllBytesAsync(savePath, bytes);
            System.Diagnostics.Debug.WriteLine($"[Banner] Saved {bytes.Length}B to {savePath}");
            return savePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Banner] Download error: {ex.Message}");
            return null;
        }
    }

    private static string GetExtFromUrl(string url)
    {
        try
        {
            var path = new Uri(url).LocalPath.ToLowerInvariant().Split('?')[0];
            if (path.EndsWith(".jpg") || path.EndsWith(".jpeg")) return ".jpg";
            if (path.EndsWith(".webp")) return ".webp";
            if (path.EndsWith(".gif"))  return ".gif";
            if (path.EndsWith(".png"))  return ".png";
        }
        catch { /* ignore */ }
        return ".jpg"; // most game covers are JPEG
    }

    // ── Compiled regex patterns ──────────────────────────────────────────────

    [GeneratedRegex(@"<script[^>]+id=""__NEXT_DATA__""[^>]*>([\s\S]*?)</script>",
        RegexOptions.IgnoreCase)]
    private static partial Regex NextDataScript();

    [GeneratedRegex(@"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex OgImagePropFirst();

    [GeneratedRegex(@"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex OgImageContentFirst();

    [GeneratedRegex(@"<meta[^>]+name=[""']twitter:image[""'][^>]+content=[""']([^""']+)[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex TwitterImage();

    [GeneratedRegex(@"<picture[^>]*>[\s\S]*?<source[^>]+srcset=""([^""]+)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex PictureSrcset();

    [GeneratedRegex(@"<img[^>]+srcset=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrcset();

    [GeneratedRegex(@"<img[^>]+class=""[^""]*object-cover[^""]*""[^>]+src=""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ObjectCoverSrc();

    [GeneratedRegex(@"<img[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AnyImgSrc();
}
