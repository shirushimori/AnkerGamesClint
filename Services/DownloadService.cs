using System.IO;
using System.Net.Http;
using AnkerGamesClient.Models;

namespace AnkerGamesClient.Services;

public class DownloadService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    static DownloadService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    /// <summary>
    /// Downloads a file with progress reporting. Reports (bytesReceived, totalBytes) — totalBytes may be -1 if unknown.
    /// </summary>
    public async Task DownloadAsync(
        string url,
        string savePath,
        string cookies,
        IProgress<(long received, long total)> progress,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrWhiteSpace(cookies))
            request.Headers.TryAddWithoutValidation("Cookie", cookies);

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long received = 0;
        int read;

        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            progress.Report((received, total));
        }
    }
}
