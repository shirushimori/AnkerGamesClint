using AnkerGamesClient.Models;
using AnkerGamesClient.Services;

namespace AnkerGamesClient.ViewModels;

public class MainViewModel : ViewModelBase
{
    public AppSettings Settings { get; }
    public LibraryViewModel LibraryViewModel { get; }
    public DownloadCenterViewModel DownloadCenterViewModel { get; }

    private readonly SettingsService _settingsService;
    private readonly ShortcutService _shortcutService;

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        Settings = _settingsService.Load();

        _shortcutService = new ShortcutService();

        var libraryService = new LibraryService(Settings.InstallPath);
        LibraryViewModel = new LibraryViewModel(libraryService);

        var downloadService = new DownloadService();
        var extractionService = new ExtractionService(Settings.SevenZipPath);
        var bannerScraper = new BannerScraperService();

        DownloadCenterViewModel = new DownloadCenterViewModel(downloadService, extractionService, bannerScraper);
        DownloadCenterViewModel.DownloadCompleted += OnDownloadCompleted;
    }

    private void OnDownloadCompleted(GameEntry entry)
    {
        LibraryViewModel.AddGame(entry);

        if (Settings.DesktopShortcut && !string.IsNullOrWhiteSpace(entry.ExePath))
            _shortcutService.CreateDesktopShortcut(entry.ExePath, entry.Name);

        if (Settings.StartShortcut && !string.IsNullOrWhiteSpace(entry.ExePath))
            _shortcutService.CreateStartMenuShortcut(entry.ExePath, entry.Name);
    }
}
