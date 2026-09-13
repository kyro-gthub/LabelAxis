using System.Printing;
using System.Windows.Documents;
using System.Windows.Documents.Serialization;
using System.Windows.Xps;

namespace LabelMaker.App.Printing;

/// <summary>Everything needed to send one job: where, how, and the sheets.</summary>
public sealed record PrintJobRequest(
    PrintQueue Queue, PrintTicket Ticket, DocumentPaginator Paginator, string Title, int Copies)
{
    public int Sheets => Paginator.PageCount;
}

public enum PrintJobOutcome
{
    Completed,
    Cancelled,
}

/// <summary>
/// Sends a job without freezing the app. <see cref="XpsDocumentWriter.WriteAsync(DocumentPaginator, PrintTicket)"/>
/// renders and spools one sheet at a time between other UI work, reports each
/// finished sheet, and can be cancelled part-way. The synchronous Write it replaces
/// locked the window for the whole job — about 20 seconds for 700 sheets.
/// </summary>
public static class PrintJob
{
    /// <param name="progress">Receives the number of sheets handed to the spooler so far.</param>
    public static Task<PrintJobOutcome> RunAsync(PrintJobRequest request, IProgress<int>? progress, CancellationToken cancellation)
    {
        var done = new TaskCompletionSource<PrintJobOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        XpsDocumentWriter writer = PrintQueue.CreateXpsDocumentWriter(request.Queue);

        writer.WritingProgressChanged += (_, e) =>
        {
            if (e.WritingLevel == WritingProgressChangeLevel.FixedPageWritingProgress)
            {
                progress?.Report(e.Number);
            }
        };

        writer.WritingCompleted += (_, e) =>
        {
            if (e.Error is not null)
            {
                done.TrySetException(e.Error);
            }
            else
            {
                done.TrySetResult(e.Cancelled ? PrintJobOutcome.Cancelled : PrintJobOutcome.Completed);
            }
        };

        writer.WritingCancelled += (_, _) => done.TrySetResult(PrintJobOutcome.Cancelled);

        CancellationTokenRegistration registration = cancellation.Register(() =>
        {
            try
            {
                writer.CancelAsync();
            }
            catch (InvalidOperationException)
            {
                // Already finished.
            }
        });

        _ = done.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);

        writer.WriteAsync(request.Paginator, request.Ticket);
        return done.Task;
    }
}
