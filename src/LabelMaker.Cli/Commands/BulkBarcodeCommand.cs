using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Bulk;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;

namespace LabelMaker.Cli.Commands;

internal static class BulkBarcodeCommand
{
    public static int Run(ArgMap o)
    {
        string outDir = o.GetRequired("out");
        Directory.CreateDirectory(outDir);
        string format = o.GetOr("format", "svg").ToLowerInvariant();
        if (format is not ("svg" or "pdf"))
        {
            throw new CliException("--format must be svg or pdf (PNG is app-only).");
        }

        var symbology = CliShared.ParseSymbology(o.Get("symbology"));
        var request = new BulkBarcodeRequest
        {
            Sequence = CliShared.BuildSequence(o),
            Template = new BarcodeSpec
            {
                Symbology = symbology,
                ModuleWidthMm = o.GetDouble("module-mm", 0.33),
                BarHeightMm = o.GetDouble("height-mm", 15.0),
                Gs1Mode = o.GetFlag("gs1") || symbology == BarcodeSymbology.GS1_128,
                HumanReadable = o.GetFlag("no-hri") ? HumanReadablePosition.None : HumanReadablePosition.Below,
            },
            Format = format == "pdf" ? BarcodeImageFormat.Pdf : BarcodeImageFormat.Svg,
            Dpi = o.GetInt("dpi", 300),
            WidthMm = o.GetDouble("width-mm", 0),
            HeightMm = o.GetDouble("box-height-mm", 0),
            TransparentBackground = o.GetFlag("transparent"),
            HumanReadable = !o.GetFlag("no-hri"),
            FileNamePattern = o.GetOr("file-name", "{value}"),
            OutputDirectory = outDir,
            MaxItems = o.GetInt("max", 200_000),
        };

        (string first, string last, long count) = BulkBarcodeService.Preview(request);
        Console.Error.WriteLine($"{count:N0} barcodes · first={first} · last={last} · -> {outDir}");

        IReadOnlyList<string> errors = BulkBarcodeService.ValidateAll(request);
        if (errors.Count > 0)
        {
            foreach (string e in errors)
            {
                Console.Error.WriteLine($"error: {e}");
            }

            throw new CliException("Validation failed; nothing written.");
        }

        var svg = new SvgExporter();
        int written = 0;
        double? boxW = request.WidthMm > 0 ? request.WidthMm : null;
        double? boxH = request.HeightMm > 0 ? request.HeightMm : null;

        foreach (BulkBarcodeItem item in BulkBarcodeService.Enumerate(request))
        {
            string ext = format;
            string path = Path.Combine(outDir, item.FileName + "." + ext);
            if (format == "svg")
            {
                File.WriteAllText(path, svg.ExportBarcode(item.Spec, item.Value, boxW, boxH, request.TransparentBackground));
            }
            else
            {
                File.WriteAllBytes(path, BarcodePdf.Render(item.Spec, item.Value, boxW, boxH));
            }

            written++;
            if (written % 500 == 0)
            {
                Console.Error.WriteLine($"  {written:N0} / {count:N0}");
            }
        }

        Console.WriteLine($"Wrote {written:N0} files to {outDir}");
        return 0;
    }
}
