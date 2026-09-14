using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LabelMaker.Core.Model;
using LabelMaker.Core.Printing;

namespace LabelMaker.App.Views;

/// <summary>
/// Every print and PDF job, newest first: what went out, where, when and whether it
/// finished. Reprint sends a job again with the settings it used; a job that stopped
/// part-way can be finished from the first sheet that never went out.
/// </summary>
public partial class PrintHistoryWindow : Window
{
    private readonly PrintHistoryStore _store;
    private readonly string _currentProjectId;

    public PrintHistoryWindow(PrintHistoryStore store, string currentProjectId)
    {
        _store = store;
        _currentProjectId = currentProjectId;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Reload();
            SearchBox.Focus();
        };
    }

    /// <summary>The job to print again, when the window closed with Reprint.</summary>
    public ReprintRequest? Result { get; private set; }

    private PrintHistoryEntry? Selected => (JobList.SelectedItem as Row)?.Entry;

    // ---- list ---------------------------------------------------------------

    private void Reload()
    {
        IReadOnlyList<PrintHistoryEntry> all = _store.Entries;
        string query = SearchBox.Text.Trim();
        int filter = FilterBox.SelectedIndex;

        var rows = all
            .Where(e => filter switch
            {
                1 => e.Kind == PrintHistoryKind.Printer,
                2 => e.Kind == PrintHistoryKind.Pdf,
                3 => e.Status != PrintHistoryStatus.Completed,
                _ => true,
            })
            .Where(e => query.Length == 0 || Matches(e, query))
            .Select(e => new Row(e, e.ProjectId == _currentProjectId))
            .ToList();

        string? keep = Selected?.Id;
        JobList.ItemsSource = rows;
        JobList.SelectedItem = rows.FirstOrDefault(r => r.Entry.Id == keep) ?? rows.FirstOrDefault();

        int printed = all.Count(e => e.Status == PrintHistoryStatus.Completed);
        HeaderSubtitle.Text = all.Count == 0
            ? "Nothing printed yet"
            : $"{all.Count:N0} job{(all.Count == 1 ? "" : "s")} · {printed:N0} completed · last on {all[0].StartedUtc.ToLocalTime():d MMM yyyy, HH:mm}";

        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = all.Count == 0
            ? "Jobs you print or export to PDF are listed here, so you can reprint them or finish one that stopped."
            : "No jobs match.";

        ShowDetails();
    }

    private static bool Matches(PrintHistoryEntry e, string query)
    {
        foreach (string? field in new[] { e.ProjectTitle, e.ProjectPath, e.PrinterName, e.TemplateName, e.OutputPath, e.DataSourceName })
        {
            if (field?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Reload();

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Reload();
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails();

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null && e.OriginalSource is FrameworkElement { DataContext: Row })
        {
            OnReprint(sender, e);
        }
    }

    // ---- details ------------------------------------------------------------

    private void ShowDetails()
    {
        PrintHistoryEntry? e = Selected;
        DetailPanel.Visibility = e is null ? Visibility.Hidden : Visibility.Visible;
        ReprintButton.IsEnabled = e is not null;
        RemoveButton.IsEnabled = e is not null;
        RemainingButton.Visibility = e is { IsIncomplete: true } ? Visibility.Visible : Visibility.Collapsed;
        OpenFileButton.Visibility = e is { Kind: PrintHistoryKind.Pdf } ? Visibility.Visible : Visibility.Collapsed;
        if (e is null)
        {
            return;
        }

        OpenFileButton.IsEnabled = e.OutputPath is { } path && File.Exists(path);
        RemainingButton.Content = $"Print sheets {e.ResumeSheet:N0}–{e.LastSheet:N0}…";

        DetailDot.Fill = StatusBrush(e.Status);
        DetailStatus.Text = e.Status switch
        {
            PrintHistoryStatus.Completed => e.Kind == PrintHistoryKind.Pdf ? "Exported" : "Printed",
            PrintHistoryStatus.Cancelled => $"Cancelled after {e.SheetsDone:N0} of {e.Sheets:N0} sheets",
            _ => $"Failed after {e.SheetsDone:N0} of {e.Sheets:N0} sheets",
        };
        DetailError.Text = e.Error ?? string.Empty;
        DetailError.Visibility = e.Error is null ? Visibility.Collapsed : Visibility.Visible;
        DetailWhen.Text = $"{e.StartedUtc.ToLocalTime():dddd d MMMM yyyy, HH:mm} · took {FormatDuration(e.DurationSeconds)}";

        DetailGrid.Children.Clear();
        DetailGrid.RowDefinitions.Clear();
        AddDetail("Project", e.ProjectTitle + (e.ProjectId == _currentProjectId ? " (open now)" : string.Empty));
        AddDetail("Saved at", e.ProjectPath is { } projectPath ? Path.GetFileName(projectPath) : "Not saved", e.ProjectPath);
        AddDetail("Sheet", e.TemplateName ?? "—");
        if (e.Kind == PrintHistoryKind.Pdf)
        {
            AddDetail("File", e.OutputPath is { } output ? Path.GetFileName(output) : "—", e.OutputPath);
        }
        else
        {
            AddDetail("Printer", e.PrinterName ?? "—");
        }
        AddDetail("Content", e.Content switch
        {
            PrintContent.VariableData => "One label per record",
            PrintContent.Pages => "Standalone pages",
            _ => "Master label on every slot",
        });
        AddDetail("Sheets", e.SheetsText);
        AddDetail("Labels", e.Labels.ToString("N0", CultureInfo.CurrentCulture));
        AddDetail("Records", e.RecordsText + (e.DataSourceName is { } src ? $" from {src}" : string.Empty));
        AddDetail("Start slot", e.Settings.StartPosition.ToString(CultureInfo.CurrentCulture) +
            (e.Settings.SkipPositions.Count > 0 ? $", skipping {string.Join(", ", e.Settings.SkipPositions)}" : string.Empty));
        AddDetail("Scale", $"{e.Settings.ScalePercent:0.#}%" + (e.Settings.Landscape ? " · landscape" : string.Empty));

        DataWarning.Text = e.ProjectId != _currentProjectId
            ? e.ProjectPath is { } p && File.Exists(p)
                ? "This job belongs to another project. Reprinting opens it first."
                : "This job's project is not open and its file cannot be found, so it cannot be reprinted."
            : string.Empty;
        DataWarning.Visibility = DataWarning.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReprintButton.IsEnabled = e.ProjectId == _currentProjectId || (e.ProjectPath is { } path2 && File.Exists(path2));
        RemainingButton.IsEnabled = ReprintButton.IsEnabled;
    }

    private void AddDetail(string label, string value, string? fullText = null)
    {
        int row = DetailGrid.RowDefinitions.Count;
        DetailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Margin = new Thickness(0, 4, 8, 4),
            Foreground = (Brush)FindResource("Br.TextMuted"),
        };
        var text = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            ToolTip = fullText,
        };
        Grid.SetRow(name, row);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        DetailGrid.Children.Add(name);
        DetailGrid.Children.Add(text);
    }

    private static string FormatDuration(double seconds) =>
        seconds < 1 ? "under a second"
        : seconds < 90 ? $"{seconds:0} s"
        : $"{seconds / 60:0.#} min";

    private Brush StatusBrush(PrintHistoryStatus status) => (Brush)FindResource(status switch
    {
        PrintHistoryStatus.Completed => "Status.Completed",
        PrintHistoryStatus.Cancelled => "Status.Cancelled",
        _ => "Status.Failed",
    });

    // ---- actions ------------------------------------------------------------

    private void OnReprint(object sender, RoutedEventArgs e) => Finish(remainingOnly: false);

    private void OnPrintRemaining(object sender, RoutedEventArgs e) => Finish(remainingOnly: true);

    private void Finish(bool remainingOnly)
    {
        if (Selected is not { } entry || !ReprintButton.IsEnabled)
        {
            return;
        }

        Result = new ReprintRequest(entry, remainingOnly);
        DialogResult = true;
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (Selected?.OutputPath is not { } path || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, "The PDF could not be opened:\n\n" + ex.Message, "LabelAxis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry)
        {
            int index = JobList.SelectedIndex;
            _store.Remove(entry.Id);
            Reload();
            if (JobList.Items.Count > 0)
            {
                JobList.SelectedIndex = Math.Min(index, JobList.Items.Count - 1);
            }
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_store.Entries.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(this, "Remove every job from the print history? This cannot be undone.", "LabelAxis",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
        {
            _store.Clear();
            Reload();
        }
    }

    /// <summary>One job as a list row.</summary>
    private sealed class Row
    {
        public Row(PrintHistoryEntry entry, bool isCurrentProject)
        {
            Entry = entry;
            Title = entry.ProjectTitle + (isCurrentProject ? " · this project" : string.Empty);
            StatusSuffix = entry.Status switch
            {
                PrintHistoryStatus.Cancelled => " — cancelled",
                PrintHistoryStatus.Failed => " — failed",
                _ => string.Empty,
            };

            string target = entry.Kind == PrintHistoryKind.Pdf
                ? "PDF " + Path.GetFileName(entry.OutputPath ?? string.Empty)
                : entry.PrinterName ?? "printer";
            Line = $"{entry.SheetsText} · {entry.RecordsText} · {target}";
            DateTime local = entry.StartedUtc.ToLocalTime().DateTime;
            WhenShort = local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.CurrentCulture)
                : local.Year == DateTime.Today.Year ? local.ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)
                : local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
            StatusBrush = entry.Status switch
            {
                PrintHistoryStatus.Completed => Completed,
                PrintHistoryStatus.Cancelled => Cancelled,
                _ => Failed,
            };
        }

        private static readonly Brush Completed = Frozen(0x1F, 0x9D, 0x55);
        private static readonly Brush Cancelled = Frozen(0xD9, 0x8A, 0x00);
        private static readonly Brush Failed = Frozen(0xD6, 0x45, 0x45);

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public PrintHistoryEntry Entry { get; }

        public string Title { get; }

        public string StatusSuffix { get; }

        public Brush StatusBrush { get; }

        public string Line { get; }

        public string WhenShort { get; }
    }
}
