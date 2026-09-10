using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Sequences;

namespace LabelMaker.Cli.Commands;

internal static class CliShared
{
    public static BarcodeSymbology ParseSymbology(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return BarcodeSymbology.Code128;
        }

        string normalized = text.Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        foreach (BarcodeSymbology value in Enum.GetValues<BarcodeSymbology>())
        {
            if (value.ToString().Replace("_", string.Empty).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return normalized.ToLowerInvariant() switch
        {
            "code128" or "128" => BarcodeSymbology.Code128,
            "gs1128" or "gs1" => BarcodeSymbology.GS1_128,
            "qr" or "qrcode" => BarcodeSymbology.QrCode,
            "datamatrix" or "dm" => BarcodeSymbology.DataMatrix,
            "ean13" => BarcodeSymbology.Ean13,
            "upca" => BarcodeSymbology.UpcA,
            "code39" or "39" => BarcodeSymbology.Code39,
            "pdf417" => BarcodeSymbology.Pdf417,
            "aztec" => BarcodeSymbology.Aztec,
            _ => throw new CliException($"Unknown symbology '{text}'. Try: code128, gs1-128, qr, datamatrix, ean13, code39, pdf417, aztec."),
        };
    }

    public static SequenceDefinition BuildSequence(ArgMap o)
    {
        var def = new SequenceDefinition
        {
            Name = o.GetOr("name", "sequence"),
            Prefix = o.GetOr("prefix", string.Empty),
            Suffix = o.GetOr("suffix", string.Empty),
            Start = o.GetLong("start", 1),
            End = o.GetLong("end", 100),
            Step = o.GetLong("step", 1),
            Padding = o.GetInt("padding", 0),
            Radix = o.GetInt("radix", 10),
            Pattern = o.Get("pattern"),
        };

        if (o.Has("count"))
        {
            def.UseCount = true;
            def.Count = o.GetLong("count", 1);
        }

        string casing = o.GetOr("casing", "asis").ToLowerInvariant();
        def.Casing = casing switch
        {
            "upper" => SequenceCasing.Upper,
            "lower" => SequenceCasing.Lower,
            _ => SequenceCasing.AsIs,
        };

        return def;
    }
}
