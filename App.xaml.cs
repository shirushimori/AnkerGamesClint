using System.Windows;

namespace AnkerGamesClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global unhandled exception handler
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{ex.Exception.Message}",
                "AnkerGames Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ex.Handled = true;
        };
    }
}
