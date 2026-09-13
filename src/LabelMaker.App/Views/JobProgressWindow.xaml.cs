using System.Diagnostics;
using System.Windows;

namespace LabelMaker.App.Views;

/// <summary>
/// Progress for any long job (printing, PDF export, big imports): what is happening,
/// how far along, an estimate of the time left, and Cancel. The job itself is a
/// delegate given a progress reporter and a cancellation token, so the same window
/// serves UI-thread spooling and background work alike.
/// </summary>
public partial class JobProgressWindow : Window, IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly Stopwatch _clock = new();
    private readonly string _unit;
    private int _total;
    private bool _finished;

    private JobProgressWindow(string heading, string unit, int total)
    {
        InitializeComponent();
        Heading.Text = heading;
        _unit = unit;
        _total = Math.Max(0, total);
        Bar.Maximum = Math.Max(1, _total);
        Report(0);
        Closing += (_, e) =>
        {
            // Closing the window mid-job means "stop", not "hide and keep going".
            if (!_finished)
            {
                e.Cancel = true;
                RequestCancel();
            }
        };
    }

    /// <summary>
    /// Runs <paramref name="job"/> with a progress window over <paramref name="owner"/>.
    /// Returns the job's result, or throws what it threw; <see cref="OperationCanceledException"/>
    /// means the user cancelled.
    /// </summary>
    public static T Run<T>(Window? owner, string heading, string unit, int total,
        Func<IProgress<int>, CancellationToken, Task<T>> job)
    {
        var window = new JobProgressWindow(heading, unit, total) { Owner = owner };
        T result = default!;
        Exception? failure = null;

        window.Loaded += async (_, _) =>
        {
            window._clock.Start();
            var progress = new Progress<int>(window.Report);
            try
            {
                result = await job(progress, window._cancel.Token);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window._finished = true;
                window.Close();
            }
        };

        window.ShowDialog();
        window.Dispose();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result;
    }

    /// <summary>
    /// Sends a print job with progress. Returns how it ended; <paramref name="sheetsDone"/>
    /// is how many sheets reached the spooler, also when the job fails or is cancelled.
    /// </summary>
    public static Printing.PrintJobOutcome RunPrint(Window? owner, Printing.PrintJobRequest request, out int sheetsDone)
    {
        string heading = request.Copies > 1
            ? $"Printing {request.Sheets:N0} sheets × {request.Copies} copies"
            : $"Printing {request.Sheets:N0} sheet{(request.Sheets == 1 ? "" : "s")}";

        Printing.CountingProgress? counter = null;
        try
        {
            return Run(owner, heading, "sheets sent to " + request.Queue.Name, request.Sheets,
                (progress, token) => Printing.PrintJob.RunAsync(request, counter = new Printing.CountingProgress(progress), token));
        }
        finally
        {
            sheetsDone = counter?.Max ?? 0;
        }
    }

    private void Report(int done)
    {
        Bar.Value = Math.Min(done, Bar.Maximum);
        Detail.Text = _total > 0 ? $"{done:N0} of {_total:N0} {_unit}" : $"{done:N0} {_unit}";

        if (done > 0 && _total > done && _clock.Elapsed.TotalSeconds > 1.5)
        {
            double perItem = _clock.Elapsed.TotalSeconds / done;
            TimeSpan left = TimeSpan.FromSeconds(perItem * (_total - done));
            Remaining.Text = left.TotalMinutes >= 1
                ? $"About {Math.Ceiling(left.TotalMinutes):0} min left"
                : $"About {Math.Max(1, Math.Round(left.TotalSeconds)):0} s left";
        }
        else if (done >= _total && _total > 0)
        {
            Remaining.Text = "Finishing…";
        }
    }

    public void Dispose()
    {
        _cancel.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        if (_cancel.IsCancellationRequested)
        {
            return;
        }

        _cancel.Cancel();
        CancelButton.IsEnabled = false;
        Remaining.Text = "Stopping after the current sheet…";
    }
}
