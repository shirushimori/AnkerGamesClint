using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AnkerGamesClient.Services;
using AnkerGamesClient.ViewModels;
using AnkerGamesClient.Views;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;

namespace AnkerGamesClient;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ControllerService _controller;
    private string _currentPageUrl = "https://ankergames.net";

    // Index of the currently highlighted library card (-1 = none)
    private int _libIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.LibraryViewModel.Games.CollectionChanged += (_, _) =>
        {
            UpdateEmptyState();
            _libIndex = -1; // reset selection when library changes
        };
        UpdateEmptyState();

        // Controller
        _controller = new ControllerService();
        WireController();
        _controller.Start();

        _ = InitWebViewAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _controller.Stop();
        _controller.Dispose();
        base.OnClosed(e);
    }

    // ─── Custom title bar ────────────────────────────────────────────────────

    private void BtnMinimise_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void BtnMaximise_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => Close();

    // ─── Library search ──────────────────────────────────────────────────────

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        _vm.LibraryViewModel.SearchQuery = string.Empty;
        LibrarySearchBox.Focus();
    }

    // ─── Controller wiring ───────────────────────────────────────────────────

    private void WireController()
    {
        _controller.TabNext  += () => Dispatcher.Invoke(ControllerTabNext);
        _controller.TabPrev  += () => Dispatcher.Invoke(ControllerTabPrev);
        _controller.NavLeft  += () => Dispatcher.Invoke(() => ControllerNavCard(-1));
        _controller.NavRight += () => Dispatcher.Invoke(() => ControllerNavCard(+1));
        _controller.NavUp    += () => Dispatcher.Invoke(() => ControllerScroll(-120));
        _controller.NavDown  += () => Dispatcher.Invoke(() => ControllerScroll(+120));
        _controller.NavConfirm += () => Dispatcher.Invoke(ControllerConfirm);
        _controller.NavBack    += () => Dispatcher.Invoke(ControllerBack);
        _controller.OpenSearch += () => Dispatcher.Invoke(ControllerOpenSearch);
        _controller.ScrollUp   += () => Dispatcher.Invoke(() => ControllerScroll(-60));
        _controller.ScrollDown += () => Dispatcher.Invoke(() => ControllerScroll(+60));

        _controller.ControllerConnected    += () =>
            Dispatcher.Invoke(() => ControllerStatusText.Visibility = Visibility.Visible);
        _controller.ControllerDisconnected += () =>
            Dispatcher.Invoke(() => ControllerStatusText.Visibility = Visibility.Collapsed);
    }

    // Switch to next tab (RB)
    private void ControllerTabNext()
    {
        var next = (MainTabs.SelectedIndex + 1) % MainTabs.Items.Count;
        MainTabs.SelectedIndex = next;
    }

    // Switch to previous tab (LB)
    private void ControllerTabPrev()
    {
        var prev = (MainTabs.SelectedIndex - 1 + MainTabs.Items.Count) % MainTabs.Items.Count;
        MainTabs.SelectedIndex = prev;
    }

    // Navigate library cards left/right (D-pad or stick)
    private void ControllerNavCard(int delta)
    {
        if (MainTabs.SelectedIndex != 1) return; // only on Library tab

        var count = _vm.LibraryViewModel.Games.Count;
        if (count == 0) return;

        _libIndex = Math.Clamp(_libIndex + delta, 0, count - 1);
        HighlightLibraryCard(_libIndex);
    }

    // Scroll the active tab's ScrollViewer
    private void ControllerScroll(double delta)
    {
        var sv = FindActiveScrollViewer();
        sv?.ScrollToVerticalOffset(sv.VerticalOffset + delta);
    }

    // A button — launch selected game or click focused element
    private void ControllerConfirm()
    {
        if (MainTabs.SelectedIndex == 1 && _libIndex >= 0 &&
            _libIndex < _vm.LibraryViewModel.Games.Count)
        {
            _vm.LibraryViewModel.Games[_libIndex].LaunchCommand.Execute(null);
        }
        else
        {
            // Simulate Enter on whatever has keyboard focus
            var focused = Keyboard.FocusedElement as UIElement;
            focused?.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(this)!,
                0, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent });
        }
    }

    // B button — clear search or go back in browser
    private void ControllerBack()
    {
        if (MainTabs.SelectedIndex == 1 &&
            !string.IsNullOrEmpty(_vm.LibraryViewModel.SearchQuery))
        {
            _vm.LibraryViewModel.SearchQuery = string.Empty;
        }
        else if (MainTabs.SelectedIndex == 0 && WebBrowser.CanGoBack)
        {
            WebBrowser.GoBack();
        }
    }

    // Start button — focus search bar on Library tab
    private void ControllerOpenSearch()
    {
        MainTabs.SelectedIndex = 1;
        LibrarySearchBox.Focus();
        LibrarySearchBox.SelectAll();
    }

    // Highlight a library card by index (scroll it into view + visual focus)
    private void HighlightLibraryCard(int index)
    {
        // Give keyboard focus to the ItemsControl container at that index
        var container = GamesList.ItemContainerGenerator
            .ContainerFromIndex(index) as FrameworkElement;
        if (container is null) return;

        container.BringIntoView();
        container.Focus();
    }

    // Find the ScrollViewer inside the currently active tab
    private ScrollViewer? FindActiveScrollViewer()
    {
        var tab = MainTabs.SelectedItem as TabItem
               ?? MainTabs.Items[MainTabs.SelectedIndex] as TabItem;
        if (tab?.Content is DependencyObject content)
            return FindChild<ScrollViewer>(content);
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    // ─── WebView2 init ──────────────────────────────────────────────────────

    private async Task InitWebViewAsync()
    {
        try
        {
            await WebBrowser.EnsureCoreWebView2Async();

            var wv = WebBrowser.CoreWebView2;
            wv.Settings.AreDefaultContextMenusEnabled = false;
            wv.Settings.AreDevToolsEnabled = false;
            wv.Settings.AreBrowserAcceleratorKeysEnabled = false;
            wv.Settings.IsStatusBarEnabled = false;

            wv.NewWindowRequested += CoreWebView2_NewWindowRequested;
            wv.DownloadStarting += CoreWebView2_DownloadStarting;
            WebBrowser.NavigationCompleted += WebBrowser_NavigationCompleted;
        }
        catch (Exception ex)
        {
            ThemedDialog.ShowError("WebView2 Error",
                "WebView2 failed to initialize.",
                ex.Message + "\n\nMake sure the WebView2 Runtime is installed.");
        }
    }

    // ─── WebView2 events ────────────────────────────────────────────────────

    private void WebBrowser_NavigationCompleted(object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        _currentPageUrl = WebBrowser.Source?.ToString() ?? _currentPageUrl;
        AddressBar.Text = _currentPageUrl;
    }

    private void CoreWebView2_NewWindowRequested(object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        WebBrowser.CoreWebView2.Navigate(e.Uri);
    }

    private void CoreWebView2_DownloadStarting(object? sender,
        CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;

        var url = e.DownloadOperation.Uri;

        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            HandleTorrent(url);
            return;
        }

        var filename = DeriveFilename(e.DownloadOperation.ResultFilePath, url);
        var pageUrl = _currentPageUrl;

        WebBrowser.CoreWebView2.ExecuteScriptAsync("document.cookie")
            .ContinueWith(t =>
            {
                var raw = string.Empty;
                if (!t.IsFaulted && t.Result is { } result)
                    raw = result.Trim('"');
                Dispatcher.Invoke(() => StartDownload(url, filename, raw, pageUrl));
            });
    }

    // ─── Download handling ──────────────────────────────────────────────────

    private void StartDownload(string url, string filename, string cookies, string pageUrl)
    {
        var installPath = _vm.Settings.InstallPath;
        Directory.CreateDirectory(installPath);
        MainTabs.SelectedIndex = 2;
        _vm.DownloadCenterViewModel.AddDownload(url, filename, installPath, cookies, pageUrl);
    }

    private void HandleTorrent(string url)
    {
        var qbt = _vm.Settings.QBittorrentPath;
        if (File.Exists(qbt))
            Process.Start(new ProcessStartInfo(qbt, $"\"{url}\"") { UseShellExecute = true });
        else
            ThemedDialog.ShowWarning("Not Found",
                "qBittorrent not found.",
                $"Expected at: {qbt}", owner: this);
    }

    private static string DeriveFilename(string? resultFilePath, string url)
    {
        if (!string.IsNullOrWhiteSpace(resultFilePath))
        {
            var n = Path.GetFileName(resultFilePath).Split('?')[0];
            if (!string.IsNullOrWhiteSpace(n) && n.Contains('.')) return n;
        }
        try
        {
            var n = Path.GetFileName(new Uri(url).LocalPath).Split('?')[0];
            if (!string.IsNullOrWhiteSpace(n) && n.Contains('.')) return n;
        }
        catch { /* ignore */ }
        return "game.rar";
    }

    // ─── Address bar ────────────────────────────────────────────────────────

    private void AddressBar_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText(_currentPageUrl);
            AddressBar.Opacity = 0.5;
            var t = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(400) };
            t.Tick += (_, _) => { AddressBar.Opacity = 1.0; t.Stop(); };
            t.Start();
        }
        catch { /* clipboard locked */ }
    }

    // ─── Browser navigation ─────────────────────────────────────────────────

    private void BrowserBack_Click(object sender, RoutedEventArgs e)
    {
        if (WebBrowser.CanGoBack) WebBrowser.GoBack();
    }

    private void BrowserForward_Click(object sender, RoutedEventArgs e)
    {
        if (WebBrowser.CanGoForward) WebBrowser.GoForward();
    }

    // ─── Progress bar click = pause / resume ────────────────────────────────

    private void ProgressBar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is ProgressBar pb && pb.Tag is DownloadItemViewModel vm)
            vm.PauseResumeCommand.Execute(null);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private void UpdateEmptyState()
    {
        EmptyLibraryText.Visibility = _vm.LibraryViewModel.Games.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
