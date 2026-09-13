using LabelMaker.Core.Formtec;

namespace LabelMaker.Core.Printing.Thermal;

/// <summary>The command language sent to a thermal label printer.</summary>
public enum ThermalLanguage
{
    /// <summary>Zebra Programming Language (Zebra and most "ZPL-compatible" printers).</summary>
    Zpl = 0,

    /// <summary>TSC's TSPL/TSPL2 (TSC, Xprinter, many desktop label printers).</summary>
    Tspl = 1,
}

/// <summary>Where the printer commands go.</summary>
public enum ThermalDestination
{
    /// <summary>A printer installed in Windows; bytes go through the spooler untouched ("raw").</summary>
    WindowsPrinter = 0,

    /// <summary>A network printer's raw port (usually 9100).</summary>
    Network = 1,

    /// <summary>A .zpl/.prn file, for sending with another tool or checking the output.</summary>
    File = 2,
}

public enum ThermalAfterPrint
{
    TearOff = 0,
    Peel = 1,
    Cut = 2,
}

/// <summary>How grey pixels (anti-aliased text, photos) become printed or blank dots.</summary>
public enum DitherMode
{
    /// <summary>Crisp: darker than the threshold prints. Best for text and barcodes.</summary>
    Threshold = 0,

    /// <summary>Error diffusion: keeps shading in photos and logos.</summary>
    Diffusion = 1,
}

/// <summary>Thermal printer settings, saved with the project.</summary>
public sealed class ThermalPrintSettings
{
    public ThermalLanguage Language { get; set; } = ThermalLanguage.Zpl;

    public ThermalDestination Destination { get; set; } = ThermalDestination.WindowsPrinter;

    public string? PrinterName { get; set; }

    /// <summary>host or host:port for <see cref="ThermalDestination.Network"/>.</summary>
    public string NetworkAddress { get; set; } = string.Empty;

    /// <summary>Print head resolution: 203, 300 or 600 dots per inch.</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>Darkness 0–30 (ZPL scale; TSPL density 0–15 is derived from it).</summary>
    public int Darkness { get; set; } = 15;

    /// <summary>Print speed in inches per second.</summary>
    public int SpeedIps { get; set; } = 4;

    /// <summary>Media sensing; null uses the template's roll setting.</summary>
    public MediaSensing? Sensing { get; set; }

    public ThermalAfterPrint AfterPrint { get; set; } = ThermalAfterPrint.TearOff;

    /// <summary>Moves the print right (positive) or left on the label, to correct a printer that is off.</summary>
    public double OffsetXMm { get; set; }

    /// <summary>Moves the print down (positive) or up.</summary>
    public double OffsetYMm { get; set; }

    /// <summary>Prints upside down, for rolls that come out of the printer the other way round.</summary>
    public bool Rotate180 { get; set; }

    public DitherMode Dither { get; set; } = DitherMode.Threshold;

    /// <summary>0–255; pixels darker than this print, in <see cref="DitherMode.Threshold"/> mode.</summary>
    public int Threshold { get; set; } = 150;

    public ThermalPrintSettings Clone() => (ThermalPrintSettings)MemberwiseClone();

    /// <summary>Dots across the given length at the print head's resolution.</summary>
    public int Dots(double mm) => (int)Math.Round(mm / 25.4 * Math.Max(1, Dpi));
}
