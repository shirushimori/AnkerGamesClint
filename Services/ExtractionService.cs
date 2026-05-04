using System.IO;
using System.IO.Compression;
using System.Diagnostics;

namespace AnkerGamesClient.Services;

public class ExtractionService
{
    private readonly string _sevenZipPath;

    public ExtractionService(string sevenZipPath)
    {
        _sevenZipPath = sevenZipPath;
    }

    /// <summary>
    /// Extracts the archive. Always tries 7-Zip first (handles ZIP, RAR, 7z, tar, etc.).
    /// Falls back to the built-in ZipFile only when 7-Zip is not available AND the file
    /// extension is .zip. Returns (success, errorDetail).
    /// </summary>
    public async Task<(bool ok, string error)> ExtractAsync(string archivePath, string extractPath)
    {
        Directory.CreateDirectory(extractPath);

        // Always prefer 7-Zip — it handles every format including ZIPs that use
        // unsupported compression methods (Deflate64, LZMA, etc.)
        var sevenZipExe = ResolveSevenZip();
        if (sevenZipExe is not null)
            return await ExtractWith7ZipAsync(archivePath, extractPath, sevenZipExe);

        // 7-Zip not found — fall back to built-in ZipFile for plain .zip only
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return await ExtractZipFallbackAsync(archivePath, extractPath);

        return (false, "7-Zip not found. Install 7-Zip or update its path in settings.");
    }

    private string? ResolveSevenZip()
    {
        // 1. Configured path
        if (File.Exists(_sevenZipPath)) return _sevenZipPath;

        // 2. Common install locations
        var candidates = new[]
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // 3. On PATH
        try
        {
            var psi = new ProcessStartInfo("7z.exe", "i")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
            if (p?.ExitCode == 0) return "7z.exe";
        }
        catch { /* not on PATH */ }

        return null;
    }

    private static Task<(bool, string)> ExtractWith7ZipAsync(
        string archivePath, string extractPath, string sevenZipExe)
    {
        return Task.Run<(bool, string)>(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // -o must be immediately followed by the path (no space) — 7-Zip requirement
                psi.ArgumentList.Add("x");
                psi.ArgumentList.Add(archivePath);
                psi.ArgumentList.Add($"-o{extractPath}");
                psi.ArgumentList.Add("-y");   // yes to all prompts

                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Could not start 7-Zip.");

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    var firstLine = detail.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                         .FirstOrDefault() ?? "unknown error";
                    Debug.WriteLine($"7-Zip exit {proc.ExitCode}: {detail}");
                    return (false, $"7-Zip exit {proc.ExitCode}: {firstLine}");
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"7-Zip error: {ex}");
                return (false, ex.Message);
            }
        });
    }

    private static Task<(bool, string)> ExtractZipFallbackAsync(
        string archivePath, string extractPath)
    {
        return Task.Run<(bool, string)>(() =>
        {
            try
            {
                ZipFile.ExtractToDirectory(archivePath, extractPath, overwriteFiles: true);
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ZIP fallback error: {ex}");
                return (false, $"ZIP: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Finds the most likely game executable inside a folder.
    /// Excludes setup/uninstall/redist executables and picks the largest remaining one.
    /// </summary>
    public static string? FindGameExe(string folder)
    {
        var excluded = new[] { "setup", "unins", "redist", "vcredist", "directx", "dxsetup" };

        var exes = Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories)
            .Where(e => !excluded.Any(x =>
                Path.GetFileNameWithoutExtension(e)
                    .Contains(x, StringComparison.OrdinalIgnoreCase)))
            .Select(e => new FileInfo(e))
            .OrderByDescending(fi => fi.Length)
            .ToList();

        return exes.FirstOrDefault()?.FullName;
    }
}
