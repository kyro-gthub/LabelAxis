using System.IO;
using System.Text;
using System.Windows.Threading;
using FluentAssertions;
using LabelMaker.App.Printing;
using LabelMaker.App.Rendering;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Printing.Thermal;
using LabelMaker.Core.Units;
using Xunit;

namespace LabelMaker.App.Tests.Printing;

public sealed class ThermalNativeJobTests
{
    /// <summary>Keeps everything "sent to the printer" in memory.</summary>
    private sealed class MemorySink : IThermalSink
    {
        public MemoryStream Bytes { get; } = new();

        public string Description => "memory";

        public Task WriteAsync(byte[] data, CancellationToken cancellation)
        {
            Bytes.Write(data);
            return Task.CompletedTask;
        }

        public Task CompleteAsync(CancellationToken cancellation) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private static string Send(AppHarness h, ThermalSendMode mode, ThermalLanguage language)
    {
        var renderer = new SheetVisualRenderer(new WpfTextMeasurer(), new WpfImageSource(new AssetStore()));
        ThermalJobPlan plan = ThermalJobPlan.Build(h.Session.Project, h.Session.Data, h.Session.Project.PrintSettings, renderer);
        var settings = new ThermalPrintSettings { Mode = mode, Language = language };
        var sink = new MemorySink();

        // The job yields to the window thread between labels, so it runs inside a dispatcher frame here.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Task<int> job = plan.SendAsync(settings, sink, 1, null, CancellationToken.None);
        var frame = new DispatcherFrame();
        job.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        job.Result.Should().Be(50);

        return Encoding.Latin1.GetString(sink.Bytes.ToArray());
    }

    private static void Design(AppHarness h)
    {
        string csv = h.FileIn("stock.csv");
        File.WriteAllLines(csv, new[] { "Product,Sku" }.Concat(Enumerable.Range(1, 50).Select(i => $"Item {i},SKU{i:00000}")));
        h.Shell.OpenFileAnswer = csv;
        h.ViewModel.ImportDataCommand.Execute(null);

        LabelDocument label = h.Session.Project.MasterLabel;
        label.SizeMm = new SizeMm(60, 40);
        label.Objects.Clear();
        label.Objects.Add(new ShapeObject { Bounds = new RectMm(1, 1, 58, 38), Stroke = ColorRgba.Black, StrokeWidthMm = 0.5 });
        label.Objects.Add(new TextObject { Content = RichTextContent.FromPlainText("FRESH STOCK"), Bounds = new RectMm(4, 3, 50, 7), FontSizePt = 14 });
        label.Objects.Add(new TextObject { Content = RichTextContent.FromPlainText("{Product}"), Bounds = new RectMm(4, 11, 50, 7), FontSizePt = 12 });
        label.Objects.Add(new BarcodeObject
        {
            Spec = new BarcodeSpec { Symbology = BarcodeSymbology.Code128, Data = "{Sku}", BarHeightMm = 10, ModuleWidthMm = 0.25 },
            Bounds = new RectMm(4, 20, 52, 16),
        });
        h.Session.Project.PrintSettings.Content = PrintContent.VariableData;
    }

    [Fact]
    public void Printer_native_zpl_stores_the_fixed_parts_once_and_sends_only_values() => AppHarness.Run(h =>
    {
        Design(h);

        string picture = Send(h, ThermalSendMode.Picture, ThermalLanguage.Zpl);
        string native = Send(h, ThermalSendMode.PrinterNative, ThermalLanguage.Zpl);

        Count(native, "~DGR:LAXBG0.GRF").Should().Be(1, "the frame and heading are stored once");
        Count(native, "^XGR:LAXBG0.GRF").Should().Be(50);
        Count(native, "^GFA").Should().Be(0, "nothing on this label needs a picture per label");
        native.Should().Contain("^FDItem 1^FS").And.Contain("^FDItem 50^FS");
        native.Should().Contain("^BCN,").And.Contain(">:SKU0>50001", "an odd run of digits starts with one in set B, as LabelAxis encodes it");
        native.Should().EndWith("^XA^IDR:LAXBG0.GRF^FS^XZ\n");
        Count(picture, "^GFA").Should().Be(50);
        native.Length.Should().BeLessThan(picture.Length / 3, "after the first label only the changing values travel");
    });

    [Fact]
    public void Printer_native_tspl_downloads_the_background_once() => AppHarness.Run(h =>
    {
        Design(h);

        string native = Send(h, ThermalSendMode.PrinterNative, ThermalLanguage.Tspl);

        Count(native, "DOWNLOAD \"LAXBG0.BMP\"").Should().Be(1);
        Count(native, "PUTBMP").Should().Be(50);
        Count(native, "PRINT 1,1").Should().Be(50);
        native.Should().Contain("\"Item 7\"").And.Contain("BARCODE").And.Contain("\"128M\"");
        native.Should().EndWith("KILL \"LAXBG0.BMP\"\r\n");
    });

    private static int Count(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
