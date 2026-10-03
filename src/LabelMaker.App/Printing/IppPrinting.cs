using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelMaker.Core.Printing.Ipp;
using LabelMaker.Export.Pdf;

namespace LabelMaker.App.Printing;

/// <summary>
/// Talks the Internet Printing Protocol to a printer on the Wi-Fi or office network. Almost
/// every network printer made in the last decade (AirPrint, Mopria and "IPP Everywhere"
/// printers) understands it, so nothing has to be installed in Windows to print to one.
/// </summary>
public static class IppClient
{
    public const int Port = 631;

    // The printer's IPP address differs by make; these cover nearly all of them.
    private static readonly string[] CommonPaths = { "/ipp/print", "/ipp", "/ipp/printer", "/printers/ipp", "/" };

    private static readonly string[] WantedAttributes =
    {
        "printer-make-and-model", "printer-name", "printer-location", "document-format-supported", "color-supported",
        "pwg-raster-document-resolution-supported", "pwg-raster-document-type-supported", "printer-state",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Asks the printer at <paramref name="host"/> what it is, trying the usual IPP addresses; null when it does not answer as a printer.</summary>
    public static async Task<IppPrinterInfo?> DescribeAsync(string host, int port, CancellationToken cancellation)
    {
        foreach (string path in CommonPaths)
        {
            string uri = $"ipp://{host}:{port}{path}";
            try
            {
                IppMessage request = IppMessage.Request(IppOperation.GetPrinterAttributes, uri)
                    .Add(IppTag.OperationGroup, IppTag.Keyword, "requested-attributes", WantedAttributes);
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                limit.CancelAfter(TimeSpan.FromSeconds(5));
                IppMessage answer = await SendAsync(uri, request.Encode(), null, limit.Token).ConfigureAwait(false);
                if (answer.IsSuccess && answer.Find("document-format-supported") is not null)
                {
                    return IppPrinterInfo.From(uri, answer);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                // Not at this address; try the next one.
            }
        }

        return null;
    }

    /// <summary>Sends one job; throws <see cref="IOException"/> with the printer's own reason when it refuses.</summary>
    public static async Task PrintAsync(IppPrinterInfo printer, byte[] document, string mimeType, int copies, string media, string jobName, CancellationToken cancellation)
    {
        IppMessage request = IppMessage.Request(IppOperation.PrintJob, printer.Uri)
            .Add(IppTag.OperationGroup, IppTag.NameWithoutLanguage, "requesting-user-name", Environment.UserName)
            .Add(IppTag.OperationGroup, IppTag.NameWithoutLanguage, "job-name", jobName)
            .Add(IppTag.OperationGroup, IppTag.MimeMediaType, "document-format", mimeType)
            .AddInteger(IppTag.JobGroup, IppTag.Integer, "copies", Math.Max(1, copies))
            .Add(IppTag.JobGroup, IppTag.Keyword, "sides", "one-sided")
            // Labels must come out at their real size: never shrink to fit.
            .Add(IppTag.JobGroup, IppTag.Keyword, "print-scaling", "none");
        if (!media.StartsWith("custom_", StringComparison.Ordinal))
        {
            request.Add(IppTag.JobGroup, IppTag.Keyword, "media", media);
        }

        IppMessage answer = await SendAsync(printer.Uri, request.Encode(), document, cancellation).ConfigureAwait(false);
        if (!answer.IsSuccess)
        {
            string reason = answer.Find("status-message")?.Text is { Length: > 0 } m ? m : $"status 0x{answer.Code:X4}";
            throw new IOException($"The printer refused the job: {reason}.");
        }
    }

    private static async Task<IppMessage> SendAsync(string ippUri, byte[] request, byte[]? document, CancellationToken cancellation)
    {
        // IPP travels as an HTTP POST: the request, then the document straight after it.
        var http = new Uri(ippUri.Replace("ipp://", "http://", StringComparison.OrdinalIgnoreCase));
        byte[] body = document is null ? request : [.. request, .. document];
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
        using HttpResponseMessage response = await Http.PostAsync(http, content, cancellation).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new IOException($"The printer answered HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
        return IppMessage.Decode(bytes);
    }
}

/// <summary>Finds printers on this PC's networks that print over IPP, and says what each one is.</summary>
public static class WifiPrinterFinder
{
    public static async Task<IReadOnlyList<IppPrinterInfo>> FindAsync(IProgress<int>? progress, CancellationToken cancellation)
    {
        // Who listens on the IPP port (80% of the progress), then ask each one what it is.
        var scan = new Progress<int>(p => progress?.Report(p * 8 / 10));
        IReadOnlyList<FoundPrinter> hosts = await NetworkPrinterFinder.FindAsync(IppClient.Port, scan, cancellation).ConfigureAwait(false);
        var infos = await Task.WhenAll(hosts.Select(h => IppClient.DescribeAsync(h.Address.ToString(), h.Port, cancellation))).ConfigureAwait(false);
        progress?.Report(100);
        return infos.OfType<IppPrinterInfo>().ToList();
    }

    /// <summary>The host name or address part of a printer's IPP address.</summary>
    public static string HostOf(IppPrinterInfo info) =>
        Uri.TryCreate(info.Uri.Replace("ipp://", "http://", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out Uri? u) ? u.Host : info.Uri;
}

/// <summary>
/// Prints a job on a network printer without a driver: each sheet the Print dialog would send
/// is drawn at the printer's resolution and sent as PWG Raster (or as PDF pictures of the
/// sheets for printers that only take PDF), so it prints exactly as the preview shows.
/// </summary>
public static class IppPrintJob
{
    /// <summary>The name a network printer goes by in the printer list and the print history.</summary>
    public static string DisplayName(IppPrinterInfo info) => $"Wi-Fi: {(info.Model.Length > 0 ? info.Model : "Printer")} ({WifiPrinterFinder.HostOf(info)})";

    /// <summary>Draws every page on the UI thread, then sends the job; reports pages drawn.</summary>
    public static async Task SendAsync(IppPrinterInfo printer, DocumentPaginator paginator, int copies, string jobName,
        IProgress<int> progress, CancellationToken cancellation)
    {
        IppDocumentFormat format = printer.BestFormat;
        if (format == IppDocumentFormat.None)
        {
            throw new IOException("This printer does not accept PWG Raster or PDF. Add it in Windows (Settings › Printers & scanners) and print to it from there.");
        }

        int dpi = format == IppDocumentFormat.PwgRaster ? printer.RasterDpi : 300;
        bool color = format != IppDocumentFormat.PwgRaster || printer.RasterInColor;
        int count = paginator.PageCount;
        string media = "iso_a4_210x297mm";

        using var raster = new MemoryStream();
        var pdfPages = new List<(byte[], double, double)>();
        if (format == IppDocumentFormat.PwgRaster)
        {
            raster.Write("RaS2"u8);
        }

        for (int i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            DocumentPage page = paginator.GetPage(i);
            double widthMm = page.Size.Width / 96.0 * 25.4;
            double heightMm = page.Size.Height / 96.0 * 25.4;
            media = PwgRaster.MediaName(widthMm, heightMm);

            if (format == IppDocumentFormat.PwgRaster)
            {
                RasterPage pixels = Draw(page, dpi, color);
                // Encoding is slow for a big sheet; do it off the UI thread.
                await Task.Run(() => PwgRaster.WritePage(raster, pixels, dpi, media, count), cancellation).ConfigureAwait(true);
            }
            else
            {
                pdfPages.Add((DrawPng(page, dpi), Math.Min(widthMm, heightMm), Math.Max(widthMm, heightMm)));
            }

            progress.Report(i + 1);

            // Let the progress window repaint between sheets.
            await Task.Yield();
        }

        byte[] document;
        if (format == IppDocumentFormat.PwgRaster)
        {
            document = raster.ToArray();
        }
        else
        {
            using var pdf = new MemoryStream();
            await Task.Run(() => RasterPdf.Write(pdf, pdfPages, jobName), cancellation).ConfigureAwait(true);
            document = pdf.ToArray();
        }

        await IppClient.PrintAsync(printer, document, format == IppDocumentFormat.PwgRaster ? "image/pwg-raster" : "application/pdf",
            copies, media, jobName, cancellation).ConfigureAwait(true);
    }

    /// <summary>The page on white at <paramref name="dpi"/>, turned upright if it is landscape (printers feed portrait).</summary>
    internal static RasterPage Draw(DocumentPage page, int dpi, bool color)
    {
        BitmapSource bitmap = Render(page, dpi);
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        var bgra = new byte[w * h * 4];
        bitmap.CopyPixels(bgra, w * 4, 0);

        bool turn = w > h;
        int outW = turn ? h : w, outH = turn ? w : h;
        int bpp = color ? 3 : 1;
        var pixels = new byte[outW * outH * bpp];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int s = ((y * w) + x) * 4;
                // Turned a quarter clockwise: the sheet's left edge becomes the top.
                (int ox, int oy) = turn ? (h - 1 - y, x) : (x, y);
                int d = ((oy * outW) + ox) * bpp;
                byte b = bgra[s], g = bgra[s + 1], r = bgra[s + 2];
                if (color)
                {
                    pixels[d] = r;
                    pixels[d + 1] = g;
                    pixels[d + 2] = b;
                }
                else
                {
                    pixels[d] = (byte)(((r * 299) + (g * 587) + (b * 114)) / 1000);
                }
            }
        }

        return new RasterPage(pixels, outW, outH, color);
    }

    private static byte[] DrawPng(DocumentPage page, int dpi)
    {
        BitmapSource bitmap = Render(page, dpi);
        if (bitmap.PixelWidth > bitmap.PixelHeight)
        {
            bitmap = new TransformedBitmap(bitmap, new RotateTransform(90));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static BitmapSource Render(DocumentPage page, int dpi)
    {
        int w = (int)Math.Round(page.Size.Width / 96.0 * dpi);
        int h = (int)Math.Round(page.Size.Height / 96.0 * dpi);
        var paper = new DrawingVisual();
        using (DrawingContext dc = paper.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, page.Size.Width, page.Size.Height));
        }

        var bitmap = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(paper);
        bitmap.Render(page.Visual);
        bitmap.Freeze();
        return bitmap;
    }
}
