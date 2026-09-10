using LabelMaker.Core.Barcodes;
using LabelMaker.Export.Pdf;
using LabelMaker.Export.Svg;

namespace LabelMaker.Cli.Commands;

internal static class BarcodeCommand
{
    public static int Run(ArgMap o)
    {
        var symbology = CliShared.ParseSymbology(o.Get("symbology"));
        string value = o.GetRequired("value");
        string outPath = o.GetRequired("out");
        string format = o.GetOr("format", InferFormat(outPath));

        var spec = new BarcodeSpec
        {
            Symbology = symbology,
            Data = value,
            ModuleWidthMm = o.GetDouble("module-mm", 0.33),
            BarHeightMm = o.GetDouble("height-mm", 15.0),
            QuietZoneModules = o.GetInt("quiet", 10),
            Gs1Mode = o.GetFlag("gs1") || symbology == BarcodeSymbology.GS1_128,
            HumanReadable = o.GetFlag("no-hri") ? HumanReadablePosition.None : HumanReadablePosition.Below,
        };

        BarcodeValidationResult validation = BarcodeValidator.Validate(spec, value);
        foreach (BarcodeIssue issue in validation.Issues)
        {
            Console.Error.WriteLine($"{issue.Severity.ToString().ToLowerInvariant()}: {issue.Message}");
        }

        if (!validation.IsValid)
        {
            throw new CliException("Barcode data failed validation.");
        }

        double? w = o.Has("width-mm") ? o.GetDouble("width-mm", 0) : null;
        double? h = o.Has("box-height-mm") ? o.GetDouble("box-height-mm", 0) : null;

        switch (format.ToLowerInvariant())
        {
            case "svg":
            {
                string svg = new SvgExporter().ExportBarcode(spec, value, w, h, o.GetFlag("transparent"));
                File.WriteAllText(outPath, svg);
                break;
            }

            case "pdf":
            {
                byte[] pdf = BarcodePdf.Render(spec, value, w, h);
                File.WriteAllBytes(outPath, pdf);
                break;
            }

            default:
                throw new CliException($"Unsupported --format '{format}'. Use svg or pdf (PNG is app-only).");
        }

        Console.WriteLine($"Wrote {outPath} ({symbology.DisplayName()} · \"{value}\")");
        return 0;
    }

    private static string InferFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "pdf",
        _ => "svg",
    };
}
