using System.Windows;
using AnkerGamesClient.ViewModels;

namespace AnkerGamesClient.Views;

public partial class DownloadCenterWindow : Window
{
    public DownloadCenterWindow(DownloadCenterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // Override close to hide instead of destroy, so downloads keep running
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
