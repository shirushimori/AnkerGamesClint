using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AnkerGamesClient.Views;

/// <summary>
/// Themed replacement for MessageBox.Show — matches the dark app theme.
/// Use the static helper methods instead of instantiating directly.
/// </summary>
public partial class ThemedDialog : Window
{
    public enum DialogKind { Info, Warning, Danger, Question }

    private bool _result;

    public ThemedDialog()
    {
        InitializeComponent();
    }

    // ── Static helpers ───────────────────────────────────────────────────────

    /// <summary>Shows an OK-only informational dialog.</summary>
    public static void ShowInfo(string title, string message, string? detail = null,
                                Window? owner = null)
    {
        var dlg = Create(title, message, detail, DialogKind.Info, owner);
        dlg.AddButton("OK", isPrimary: true, isDanger: false, isCancel: false);
        dlg.ShowDialog();
    }

    /// <summary>Shows an OK-only warning dialog.</summary>
    public static void ShowWarning(string title, string message, string? detail = null,
                                   Window? owner = null)
    {
        var dlg = Create(title, message, detail, DialogKind.Warning, owner);
        dlg.AddButton("OK", isPrimary: true, isDanger: false, isCancel: false);
        dlg.ShowDialog();
    }

    /// <summary>Shows an OK-only error dialog.</summary>
    public static void ShowError(string title, string message, string? detail = null,
                                 Window? owner = null)
    {
        var dlg = Create(title, message, detail, DialogKind.Danger, owner);
        dlg.AddButton("OK", isPrimary: true, isDanger: false, isCancel: false);
        dlg.ShowDialog();
    }

    /// <summary>Shows a Yes/No confirmation dialog. Returns true if Yes was clicked.</summary>
    public static bool Confirm(string title, string message, string? detail = null,
                               bool isDanger = false, Window? owner = null)
    {
        var dlg = Create(title, message, detail,
                         isDanger ? DialogKind.Danger : DialogKind.Question, owner);
        dlg.AddButton("Yes", isPrimary: true, isDanger: isDanger, isCancel: false);
        dlg.AddButton("No",  isPrimary: false, isDanger: false, isCancel: true);
        dlg.ShowDialog();
        return dlg._result;
    }

    // ── Factory ──────────────────────────────────────────────────────────────

    private static ThemedDialog Create(string title, string message, string? detail,
                                       DialogKind kind, Window? owner)
    {
        var dlg = new ThemedDialog();

        if (owner is not null)
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;

        if (!string.IsNullOrWhiteSpace(detail))
        {
            dlg.DetailText.Text = detail;
            dlg.DetailText.Visibility = Visibility.Visible;
        }

        // Accent bar colour by kind
        dlg.AccentBar.Background = kind switch
        {
            DialogKind.Warning  => new SolidColorBrush(Color.FromRgb(0xf3, 0x9c, 0x12)),
            DialogKind.Danger   => new SolidColorBrush(Color.FromRgb(0xc0, 0x39, 0x2b)),
            DialogKind.Question => new SolidColorBrush(Color.FromRgb(0x7c, 0x6a, 0xf7)),
            _                   => new SolidColorBrush(Color.FromRgb(0x27, 0xae, 0x60)),
        };

        return dlg;
    }

    // ── Button builder ───────────────────────────────────────────────────────

    private void AddButton(string label, bool isPrimary, bool isDanger, bool isCancel)
    {
        var btn = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };

        var styleName = isDanger ? "DlgBtnDanger"
                      : isPrimary ? "DlgBtn"
                      : "DlgBtnSecondary";

        btn.Style = (Style)FindResource(styleName);

        btn.Click += (_, _) =>
        {
            _result = !isCancel;
            Close();
        };

        ButtonPanel.Children.Add(btn);
    }

    // ── Title bar close ──────────────────────────────────────────────────────

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}
