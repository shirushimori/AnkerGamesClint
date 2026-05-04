using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AnkerGamesClient.Commands;
using AnkerGamesClient.Models;
using AnkerGamesClient.Services;
using AnkerGamesClient.Views;

namespace AnkerGamesClient.ViewModels;

public class LibraryViewModel : ViewModelBase
{
    private readonly LibraryService _libraryService;

    // Full unfiltered list
    private readonly ObservableCollection<GameEntryViewModel> _allGames = [];

    // Filtered list — what the UI binds to
    public ObservableCollection<GameEntryViewModel> Games { get; } = [];

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            SetField(ref _searchQuery, value);
            ApplyFilter();
        }
    }

    public LibraryViewModel(LibraryService libraryService)
    {
        _libraryService = libraryService;
        LoadGames();
    }

    public void LoadGames()
    {
        _allGames.Clear();
        Games.Clear();
        foreach (var entry in _libraryService.LoadAll())
        {
            var vm = new GameEntryViewModel(entry, this);
            _allGames.Add(vm);
            Games.Add(vm);
        }
    }

    public void AddGame(GameEntry entry)
    {
        _libraryService.AddGame(entry);
        var existing = _allGames.FirstOrDefault(g =>
            g.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _allGames.Remove(existing);
            Games.Remove(existing);
        }
        var vm = new GameEntryViewModel(entry, this);
        _allGames.Add(vm);
        ApplyFilter(); // re-filter so new game appears if it matches
    }

    public void RemoveGame(GameEntryViewModel vm)
    {
        _libraryService.RemoveGame(vm.Name);
        _allGames.Remove(vm);
        Games.Remove(vm);
    }

    private void ApplyFilter()
    {
        var q = _searchQuery.Trim();
        Games.Clear();

        if (string.IsNullOrWhiteSpace(q))
        {
            foreach (var g in _allGames) Games.Add(g);
            return;
        }

        // Chunk-based: every space-separated word must appear somewhere in the name
        var chunks = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var g in _allGames)
        {
            if (chunks.All(c => g.Name.Contains(c, StringComparison.OrdinalIgnoreCase)))
                Games.Add(g);
        }
    }
}

public class GameEntryViewModel : ViewModelBase
{
    private readonly LibraryViewModel _parent;

    public string Name { get; }
    public string ExePath { get; }
    public string BannerPath { get; }
    public string PageUrl { get; }

    public string GameFolder =>
        string.IsNullOrWhiteSpace(ExePath)
            ? string.Empty
            : Path.GetDirectoryName(ExePath) ?? string.Empty;

    public RelayCommand LaunchCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand UninstallCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand SearchOnlineCommand { get; }

    public GameEntryViewModel(GameEntry entry, LibraryViewModel parent)
    {
        _parent = parent;
        Name = entry.Name;
        ExePath = entry.ExePath;
        BannerPath = entry.BannerPath;
        PageUrl = entry.PageUrl;

        LaunchCommand = new RelayCommand(Launch);
        DeleteCommand = new RelayCommand(RemoveFromLibrary);
        UninstallCommand = new RelayCommand(Uninstall);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        SearchOnlineCommand = new RelayCommand(SearchOnline);
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private void Launch()
    {
        if (string.IsNullOrWhiteSpace(ExePath))
        {
            ThemedDialog.ShowWarning("Launch Error",
                "No executable path set for this game.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ThemedDialog.ShowError("Launch Error",
                $"Could not launch \"{Name}\".", ex.Message);
        }
    }

    private void RemoveFromLibrary()
    {
        if (ThemedDialog.Confirm("Remove Game",
                $"Remove \"{Name}\" from your library?",
                "Game files will NOT be deleted.",
                isDanger: false))
            _parent.RemoveGame(this);
    }

    private void Uninstall()
    {
        var folder = FindGameRootFolder();

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ThemedDialog.ShowWarning("Uninstall Error", "Game folder not found.");
            return;
        }

        if (!ThemedDialog.Confirm("Uninstall Game",
                $"Permanently delete \"{Name}\" and all its files?",
                $"Folder: {folder}\n\nThis cannot be undone.",
                isDanger: true))
            return;

        try
        {
            Directory.Delete(folder, recursive: true);
            _parent.RemoveGame(this);
        }
        catch (Exception ex)
        {
            ThemedDialog.ShowError("Uninstall Error",
                "Could not delete game files.", ex.Message);
        }
    }

    private void OpenFolder()
    {
        var folder = GameFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            ThemedDialog.ShowWarning("Open Folder", "Game folder not found.");
            return;
        }
        try
        {
            if (!string.IsNullOrWhiteSpace(ExePath) && File.Exists(ExePath))
                Process.Start("explorer.exe", $"/select,\"{ExePath}\"");
            else
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ThemedDialog.ShowError("Open Folder", "Could not open folder.", ex.Message);
        }
    }

    private void SearchOnline()
    {
        var url = $"https://www.google.com/search?q={Uri.EscapeDataString(Name + " game")}";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            ThemedDialog.ShowError("Search Online", "Could not open browser.", ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string FindGameRootFolder()
    {
        if (string.IsNullOrWhiteSpace(ExePath)) return string.Empty;
        var dir = new DirectoryInfo(Path.GetDirectoryName(ExePath) ?? string.Empty);
        if (!dir.Exists) return string.Empty;

        var current = dir;
        while (current.Parent is not null)
        {
            if (File.Exists(Path.Combine(current.Parent.FullName, "library.json")))
                return current.FullName;
            current = current.Parent;
        }
        return dir.FullName;
    }
}
