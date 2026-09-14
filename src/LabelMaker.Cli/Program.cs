using System.Globalization;
using LabelMaker.Cli;
using LabelMaker.Cli.Commands;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Help.Print();
    return 0;
}

string command = args[0].ToLowerInvariant();
ArgMap opts = ArgMap.Parse(args.Skip(1));

try
{
    return command switch
    {
        "barcode" => BarcodeCommand.Run(opts),
        "sequence" => SequenceCommand.Run(opts),
        "bulk-barcode" => BulkBarcodeCommand.Run(opts),
        "export" => ExportCommand.Run(opts),
        "templates" => TemplatesCommand.Run(opts),
        "preflight" => PreflightCommand.Run(opts),
        "inspect" => InspectCommand.Run(opts),
        "version" or "--version" => PrintVersion(),
        _ => Unknown(command),
    };
}
catch (CliException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static int PrintVersion()
{
    Console.WriteLine("LabelAxis CLI 1.0.0");
    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"error: unknown command '{command}'. Run 'labelaxis help'.");
    return 2;
}

internal static class Help
{
    public static void Print()
    {
        Console.WriteLine(
            """
            LabelAxis CLI — headless barcode, sequence, export and inspection tools.

            USAGE
              labelaxis <command> [options]

            COMMANDS
              barcode        Render one barcode to SVG or PDF.
              sequence       Print a running number / code series.
              bulk-barcode   Render a whole barcode series to a folder (SVG/PDF).
              export         Render a .labelproj to a sheet / label / record PDF.
              templates      List or show built-in Formtec templates.
              preflight      Run pre-print validation on a .labelproj (+ optional CSV).
              inspect        Summarise a .labelproj file.
              version        Print the version.

            EXAMPLES
              labelaxis barcode --symbology code128 --value ABC001459 --out code.svg
              labelaxis sequence --prefix ABC --start 1 --end 10000 --padding 6
              labelaxis bulk-barcode --symbology code128 --prefix ABC --start 1 --end 500 \
                         --padding 6 --format svg --out ./out
              labelaxis export --project labels.labelproj --data rows.csv --out sheet.pdf
              labelaxis templates list
              labelaxis templates show formtec-3100

            NOTES
              PNG/JPEG output is only available from the desktop app (needs the Windows
              rasteriser). The CLI emits vector SVG and PDF.
            """);
    }
}

internal static class CultureBootstrap
{
    static CultureBootstrap() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
}
