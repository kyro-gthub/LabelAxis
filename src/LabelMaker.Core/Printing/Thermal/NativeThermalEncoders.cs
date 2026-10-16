using LabelMaker.Core.Formtec;

namespace LabelMaker.Core.Printing.Thermal;

/// <summary>
/// Writes a <see cref="ThermalSendMode.PrinterNative"/> job: the stored backgrounds once, then
/// each row as the printer's own text and barcode commands, then a clean-up of what was stored.
/// </summary>
public interface INativeThermalEncoder
{
    ThermalLanguage Language { get; }

    /// <summary>False when the printer language cannot carry this element (a character it has no way to send).</summary>
    bool CanCarry(NativeElement element);

    /// <summary>Printer set-up and the backgrounds to store, one per design; null entries are designs with no fixed content.</summary>
    byte[] BeginJob(IReadOnlyList<MonochromeImage?> backgrounds, ThermalPrintSettings settings, RollMedia roll, int rowWidthDots, double labelHeightMm);

    byte[] EncodeRow(NativeRow row, IReadOnlyList<MonochromeImage?> backgrounds, ThermalPrintSettings settings, RollMedia roll, double labelHeightMm, int copies);

    /// <summary>Frees the printer memory the backgrounds used.</summary>
    byte[] EndJob(IReadOnlyList<MonochromeImage?> backgrounds);
}

public static class NativeThermalEncoders
{
    public static INativeThermalEncoder For(ThermalLanguage language) => language switch
    {
        ThermalLanguage.Tspl => new TsplNativeEncoder(),
        _ => new ZplNativeEncoder(),
    };

    /// <summary>The name a design's background is stored under on the printer (8.3, shared by both languages).</summary>
    internal static string BackgroundName(int index) => "LAXBG" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
