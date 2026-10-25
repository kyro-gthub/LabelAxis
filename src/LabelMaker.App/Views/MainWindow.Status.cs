using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace LabelMaker.App.Views;

/// <summary>
/// The status bar's message: what the last action did ("Removed 4 pages…"), shown in place of
/// the label sheet for a few seconds, then the sheet comes back. A click hides it at once.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan StatusMessageTime = TimeSpan.FromSeconds(10);

    private DispatcherTimer? _statusTimer;

    private void ShowStatusMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            HideStatusMessage();
            return;
        }

        // Most messages are put together in English and translated here; ones made with
        // Localizer.F already are, and come through unchanged.
        string text = Localization.Localizer.T(message);
        StatusMessageText.Text = text;
        StatusMessagePanel.ToolTip = text;
        StatusMessagePanel.Visibility = Visibility.Visible;
        SheetPanel.Visibility = Visibility.Collapsed;

        _statusTimer ??= CreateStatusTimer();
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private DispatcherTimer CreateStatusTimer()
    {
        var timer = new DispatcherTimer { Interval = StatusMessageTime };
        timer.Tick += (_, _) => HideStatusMessage();
        return timer;
    }

    private void HideStatusMessage()
    {
        _statusTimer?.Stop();
        StatusMessagePanel.Visibility = Visibility.Collapsed;
        SheetPanel.Visibility = Visibility.Visible;
    }

    private void OnStatusMessageClick(object sender, MouseButtonEventArgs e) => HideStatusMessage();
}
