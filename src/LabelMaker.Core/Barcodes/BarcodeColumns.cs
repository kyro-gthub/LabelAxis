using System.Text.RegularExpressions;

namespace LabelMaker.Core.Barcodes;

/// <summary>
/// Tells from a column of imported records whether it holds barcodes, and which kind, so a
/// field added to the label can become a real barcode instead of its digits as text.
/// </summary>
public static partial class BarcodeColumns
{
    /// <summary>How many values are looked at; a column of thousands answers the same way.</summary>
    private const int Sample = 200;

    // Column names that say "barcode" in the languages LabelAxis speaks, and the usual code names.
    [GeneratedRegex(@"barcode|bar code|\bean\b|\bupc\b|gtin|isbn|\bqr\b|sscc|\bitf\b|code ?128|code ?39|باركود|بارکد|باڕکۆد|条码|条形码|штрих|code-barres|c[oó]digo de barras|streepjescode|बारकोड",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BarcodeName();

    [GeneratedRegex(@"\bqr\b|\burl\b|link|website|web site|رابط|لینک|网址|ссылка", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkName();

    // Fixed-length numbers each barcode type takes, longest check first.
    private static readonly (int Length, BarcodeSymbology Symbology)[] ByLength =
    {
        (14, BarcodeSymbology.Itf14),
        (13, BarcodeSymbology.Ean13),
        (12, BarcodeSymbology.UpcA),
        (8, BarcodeSymbology.Ean8),
    };

    /// <summary>
    /// The barcode type the column <paramref name="name"/> with <paramref name="values"/> is, or
    /// null when it holds ordinary text. Product numbers of a fixed length whose check digits are
    /// all right are EAN, UPC or ITF-14 whatever the column is called; links in a column named for
    /// QR codes or links are QR codes; anything else in a column named "barcode" is Code 128.
    /// </summary>
    public static BarcodeSymbology? Suggest(string name, IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        name ??= string.Empty;
        List<string> sample = values.Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0).Take(Sample).ToList();
        bool named = BarcodeName().IsMatch(name);
        if (sample.Count == 0)
        {
            return named ? BarcodeSymbology.Code128 : null;
        }

        if (sample.All(v => v.All(char.IsAsciiDigit)))
        {
            foreach ((int length, BarcodeSymbology symbology) in ByLength)
            {
                if (sample.All(v => v.Length == length) && sample.All(v => Fits(symbology, v)))
                {
                    return symbology == BarcodeSymbology.Ean13 && sample.All(v => v.StartsWith("978", StringComparison.Ordinal) || v.StartsWith("979", StringComparison.Ordinal))
                        ? BarcodeSymbology.Isbn13
                        : symbology;
                }
            }
        }

        if (sample.All(v => v.StartsWith('(')) && sample.All(v => Fits(BarcodeSymbology.GS1_128, v)))
        {
            return BarcodeSymbology.GS1_128;
        }

        bool links = sample.All(v => v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                     v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                     v.StartsWith("www.", StringComparison.OrdinalIgnoreCase));
        if (links && (named || LinkName().IsMatch(name)))
        {
            return BarcodeSymbology.QrCode;
        }

        if (!named)
        {
            return null;
        }

        // Code 128 holds any printable ASCII; other text (other alphabets, long values) goes in a QR code.
        return sample.All(v => v.Length <= 48 && v.All(c => c is >= ' ' and <= '~'))
            ? BarcodeSymbology.Code128
            : BarcodeSymbology.QrCode;
    }

    /// <summary>How many of <paramref name="values"/> a barcode of <paramref name="symbology"/> cannot hold.</summary>
    public static int Unfit(BarcodeSymbology symbology, IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0).Take(Sample)
            .Count(v => !Fits(symbology, v));
    }

    private static bool Fits(BarcodeSymbology symbology, string value) =>
        BarcodeValidator.Validate(new BarcodeSpec { Symbology = symbology, Data = value }, value).IsValid;
}
