using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using AnkerGamesClient.Commands;
using AnkerGamesClient.Models;
using AnkerGamesClient.Services;

namespace AnkerGamesClient.ViewModels;

public class DownloadCenterViewModel : ViewModelBase
{
    private readonly DownloadService _downloadService;
    private readonly ExtractionService _extractionService;
    private readonly BannerScraperService _bannerScraper;

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = [];

    public event Action<GameEntry>? DownloadCompleted;

    /// <summary>Removes all finished/cancelled/errored entries from the list.</summary>
    public RelayCommand ClearCompletedCommand { get; }

    public DownloadCenterViewModel(
        DownloadService downloadService,
        ExtractionService extractionService,
        BannerScraperService bannerScraper)
    {
        _downloadService = downloadService;
        _extractionService = extractionService;
        _bannerScraper = bannerScraper;

        ClearCompletedCommand = new RelayCommand(() =>
        {
            var done = Downloads
                .Where(d => d.Status is "Done" or "Cancelled" ||
                            d.Status.StartsWith("Error") ||
                            d.Status.StartsWith("Extract failed"))
                .ToList();
            foreach (var d in done) Downloads.Remove(d);
        });
    }

    public void AddDownload(string url, string filename, string installPath,
                            string cookies, string pageUrl = "")
    {
        var item = new DownloadItem { FileName = filename, Url = url };
        var vm = new DownloadItemViewModel(item);

        vm.CancelRequested += () =>
        {
            item.Cts.Cancel();
            item.PauseCts.Cancel();
        };

        vm.PauseResumeRequested += () =>
        {
            if (item.IsPaused)
            {
                item.IsPaused = false;
                item.PauseCts = new CancellationTokenSource();
                item.Status = "Downloading";
                _ = RunDownloadAsync(item, url, filename, installPath, cookies, pageUrl);
            }
            else
            {
                item.IsPaused = true;
                item.PauseCts.Cancel();
                item.Status = "Paused";
            }
        };

        Downloads.Add(vm);

        // Scrape banner immediately in parallel so the card shows art while downloading
        if (!string.IsNullOrWhiteSpace(pageUrl))
        {
            _ = Task.Run(async () =>
            {
                // We need a temp folder to save the banner before extraction exists.
                // Use a sibling "_banners" staging folder under installPath.
                var stagingDir = Path.Combine(installPath, "_banners",
                    Path.GetFileNameWithoutExtension(filename));
                var bannerPath = await _bannerScraper.ScrapeAndSaveAsync(pageUrl, stagingDir)
                                 ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(bannerPath))
                    item.BannerPath = bannerPath;
            });
        }

        _ = RunDownloadAsync(item, url, filename, installPath, cookies, pageUrl);
    }

    private async Task RunDownloadAsync(
        DownloadItem item, string url, string filename,
        string installPath, string cookies, string pageUrl)
    {
        var savePath = Path.Combine(installPath, filename);
        long lastBytes = 0;
        var lastTick = DateTime.UtcNow;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            item.Cts.Token, item.PauseCts.Token);

        var progress = new Progress<(long received, long total)>(t =>
        {
            var (received, total) = t;
            item.ReceivedBytes = received;
            if (total > 0)
            {
                item.TotalBytes = total;
                item.Progress = (int)(received * 100 / total);
            }

            var now = DateTime.UtcNow;
            var dt = (now - lastTick).TotalSeconds;
            if (dt >= 0.5)
            {
                var delta = received - lastBytes;
                var mbps = delta / dt / 1024.0 / 1024.0;
                item.Speed = $"{mbps:F2} MB/s";

                if (total > 0 && delta > 0)
                {
                    var remaining = total - received;
                    var etaSec = remaining / (delta / dt);
                    item.Eta = etaSec < 60
                        ? $"{etaSec:F0}s"
                        : $"{etaSec / 60:F0}m {etaSec % 60:F0}s";
                }

                lastBytes = received;
                lastTick = now;
            }
        });

        try
        {
            if (!item.IsPaused) item.Status = "Downloading";

            await _downloadService.DownloadAsync(url, savePath, cookies, progress, linked.Token);

            if (item.IsPaused) return;   // paused mid-download — resume will restart

            item.Progress = 100;
            item.Status = "Extracting...";
            item.CanCancel = false;

            var gameName = Path.GetFileNameWithoutExtension(filename);
            var extractPath = Path.Combine(installPath, gameName);
            var (ok, errorDetail) = await _extractionService.ExtractAsync(savePath, extractPath);

            if (!ok)
            {
                item.Status = $"Extract failed: {errorDetail}";
                return;
            }

            // Delete the archive file now that extraction succeeded
            try { File.Delete(savePath); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not delete archive: {ex.Message}");
            }

            item.Status = "Fetching banner...";
            var bannerPath = string.Empty;

            // Check if the parallel pre-scrape already got a banner
            if (!string.IsNullOrWhiteSpace(item.BannerPath) && File.Exists(item.BannerPath))
            {
                // Move from staging folder into the real assets folder
                var assetsDir = Path.Combine(extractPath, "assets");
                Directory.CreateDirectory(assetsDir);
                var dest = Path.Combine(assetsDir, Path.GetFileName(item.BannerPath));
                try
                {
                    File.Move(item.BannerPath, dest, overwrite: true);
                    // Clean up staging dir if empty
                    var stagingDir = Path.GetDirectoryName(item.BannerPath);
                    if (stagingDir is not null && Directory.Exists(stagingDir) &&
                        !Directory.EnumerateFileSystemEntries(stagingDir).Any())
                        Directory.Delete(stagingDir, recursive: true);
                    bannerPath = dest;
                }
                catch
                {
                    bannerPath = item.BannerPath; // keep staging path as fallback
                }
            }
            else if (!string.IsNullOrWhiteSpace(pageUrl))
            {
                // Pre-scrape didn't finish in time — scrape now
                bannerPath = await _bannerScraper.ScrapeAndSaveAsync(pageUrl, extractPath)
                             ?? string.Empty;
            }

            item.BannerPath = bannerPath;

            var exe = ExtractionService.FindGameExe(extractPath);
            item.Status = "Done";

            var entry = new GameEntry
            {
                Name = gameName,
                ExePath = exe ?? string.Empty,
                BannerPath = bannerPath,
                PageUrl = pageUrl
            };

            Application.Current.Dispatcher.Invoke(() => DownloadCompleted?.Invoke(entry));
        }
        catch (OperationCanceledException)
        {
            if (!item.IsPaused)
            {
                item.Status = "Cancelled";
                item.CanCancel = false;
            }
        }
        catch (Exception ex)
        {
            item.Status = $"Error: {ex.Message}";
            item.CanCancel = false;
        }
    }
}

/// <summary>
/// ViewModel for a single download card.
/// Every property that XAML binds to has a real setter so WPF never
/// attempts a TwoWay binding on a read-only property.
/// </summary>
public class DownloadItemViewModel : ViewModelBase
{
    private readonly DownloadItem _model;

    // ── Simple forwarding properties with explicit setters ──────────────────

    private string _fileName = string.Empty;
    public string FileName
    {
        get => _fileName;
        private set => SetField(ref _fileName, value);
    }

    private string _bannerPath = string.Empty;
    public string BannerPath
    {
        get => _bannerPath;
        private set => SetField(ref _bannerPath, value);
    }

    private int _progress;
    public int Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    private string _speed = "-- MB/s";
    public string Speed
    {
        get => _speed;
        private set => SetField(ref _speed, value);
    }

    private string _status = "Queued";
    public string Status
    {
        get => _status;
        private set
        {
            SetField(ref _status, value);
            OnPropertyChanged(nameof(PauseResumeLabel));
            OnPropertyChanged(nameof(CanPauseResume));
        }
    }

    private string _eta = string.Empty;
    public string Eta
    {
        get => _eta;
        private set => SetField(ref _eta, value);
    }

    private string _receivedFmt = "0 B";
    public string ReceivedBytesFormatted
    {
        get => _receivedFmt;
        private set => SetField(ref _receivedFmt, value);
    }

    private string _totalFmt = "?";
    public string TotalBytesFormatted
    {
        get => _totalFmt;
        private set => SetField(ref _totalFmt, value);
    }

    private bool _canCancel = true;
    public bool CanCancel
    {
        get => _canCancel;
        private set
        {
            SetField(ref _canCancel, value);
            OnPropertyChanged(nameof(CanPauseResume));
        }
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            SetField(ref _isPaused, value);
            OnPropertyChanged(nameof(PauseResumeLabel));
            OnPropertyChanged(nameof(CanPauseResume));
        }
    }

    // ── Derived ─────────────────────────────────────────────────────────────

    public string PauseResumeLabel => _isPaused ? "Resume" : "Pause";

    public bool CanPauseResume =>
        _canCancel &&
        _status is not ("Extracting..." or "Fetching banner..." or "Done" or "Cancelled") &&
        !_status.StartsWith("Extract failed") &&
        !_status.StartsWith("Error");

    // ── Commands ─────────────────────────────────────────────────────────────

    public RelayCommand CancelCommand { get; }
    public RelayCommand PauseResumeCommand { get; }

    public event Action? CancelRequested;
    public event Action? PauseResumeRequested;

    public DownloadItemViewModel(DownloadItem model)
    {
        _model = model;
        _fileName = model.FileName;

        // Mirror every model change into our own notifying properties
        _model.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(DownloadItem.FileName):       FileName = _model.FileName; break;
                case nameof(DownloadItem.BannerPath):     BannerPath = _model.BannerPath; break;
                case nameof(DownloadItem.Progress):       Progress = _model.Progress; break;
                case nameof(DownloadItem.Speed):          Speed = _model.Speed; break;
                case nameof(DownloadItem.Status):         Status = _model.Status; break;
                case nameof(DownloadItem.Eta):            Eta = _model.Eta; break;
                case nameof(DownloadItem.CanCancel):      CanCancel = _model.CanCancel; break;
                case nameof(DownloadItem.IsPaused):       IsPaused = _model.IsPaused; break;
                case nameof(DownloadItem.ReceivedBytes):
                    ReceivedBytesFormatted = _model.ReceivedBytesFormatted; break;
                case nameof(DownloadItem.TotalBytes):
                    TotalBytesFormatted = _model.TotalBytesFormatted; break;
            }
        };

        CancelCommand = new RelayCommand(
            () => CancelRequested?.Invoke(),
            () => CanCancel);

        PauseResumeCommand = new RelayCommand(
            () => PauseResumeRequested?.Invoke(),
            () => CanPauseResume);
    }
}
