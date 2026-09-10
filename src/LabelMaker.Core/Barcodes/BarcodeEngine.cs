using System.Collections.Concurrent;
using LabelMaker.Core.Barcodes.Code128;

namespace LabelMaker.Core.Barcodes;

/// <summary>
/// The single entry point for turning a <see cref="BarcodeSpec"/> + value into a
/// <see cref="BarcodeSymbol"/>. Routes the Code 128 family to the first-party
/// encoder and everything else to ZXing. Also reports which symbologies the
/// running build can actually produce (spec §6: no stubbed menu items).
/// </summary>
public static class BarcodeEngine
{
    private static readonly ConcurrentDictionary<BarcodeSymbology, bool> SupportCache = new();

    private static readonly IReadOnlyDictionary<BarcodeSymbology, string> SelfTestSamples =
        new Dictionary<BarcodeSymbology, string>
        {
            [BarcodeSymbology.Code128] = "ABC-12345",
            [BarcodeSymbology.Code128A] = "ABC123",
            [BarcodeSymbology.Code128B] = "abc123",
            [BarcodeSymbology.Code128C] = "12345678",
            [BarcodeSymbology.GS1_128] = "(01)09501101020917",
            [BarcodeSymbology.Code39] = "ABC123",
            [BarcodeSymbology.Code93] = "ABC123",
            [BarcodeSymbology.Ean13] = "5901234123457",
            [BarcodeSymbology.Ean8] = "96385074",
            [BarcodeSymbology.UpcA] = "036000291452",
            [BarcodeSymbology.UpcE] = "01234565",
            [BarcodeSymbology.Itf14] = "1540014128876",
            [BarcodeSymbology.Interleaved2of5] = "1234567890",
            [BarcodeSymbology.Codabar] = "A40156A",
            [BarcodeSymbology.Isbn13] = "9781234567897",
            [BarcodeSymbology.MsiPlessey] = "1234567",
            [BarcodeSymbology.QrCode] = "https://example.com/label/1",
            [BarcodeSymbology.DataMatrix] = "DM-TEST-001",
            [BarcodeSymbology.Pdf417] = "PDF417 TEST 001",
            [BarcodeSymbology.Aztec] = "AZTEC-TEST-001",
        };

    /// <summary>Symbologies this build can encode, verified by a one-off self-test.</summary>
    public static IReadOnlyList<BarcodeSymbology> SupportedSymbologies() =>
        Enum.GetValues<BarcodeSymbology>().Where(IsSupported).ToList();

    public static bool IsSupported(BarcodeSymbology symbology) =>
        SupportCache.GetOrAdd(symbology, RunSelfTest);

    public static BarcodeSymbol Encode(BarcodeSpec spec, string resolvedValue)
    {
        ArgumentNullException.ThrowIfNull(spec);
        string value = (resolvedValue ?? string.Empty).Trim();

        if (spec.Symbology.IsCode128Family())
        {
            string prepared = PrepareCode128Value(spec, value);
            return Code128Encoder.Encode(prepared, spec.Symbology, spec.QuietZoneModules, spec.Gs1Mode);
        }

        return ZXingEncoder.Encode(spec, value);
    }

    public static bool TryEncode(
        BarcodeSpec spec, string resolvedValue,
        out BarcodeSymbol? symbol, out BarcodeValidationResult validation)
    {
        validation = BarcodeValidator.Validate(spec, resolvedValue);
        if (!validation.IsValid)
        {
            symbol = null;
            return false;
        }

        try
        {
            symbol = Encode(spec, resolvedValue);
            return true;
        }
        catch (Exception ex)
        {
            validation.Issues.Add(new BarcodeIssue(BarcodeIssueSeverity.Error, ex.Message));
            symbol = null;
            return false;
        }
    }

    private static string PrepareCode128Value(BarcodeSpec spec, string value)
    {
        // Map the GS1 "(nn)" AI syntax to inline FNC1 markers for our encoder.
        if (!spec.Gs1Mode && spec.Symbology != BarcodeSymbology.GS1_128)
        {
            return value;
        }

        if (!value.Contains('('))
        {
            return value; // already raw; encoder prepends FNC1
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(Code128Encoder.Fnc1);
        int i = 0;
        while (i < value.Length)
        {
            if (value[i] == '(')
            {
                int close = value.IndexOf(')', i);
                if (close < 0)
                {
                    sb.Append(value[i..]);
                    break;
                }

                string ai = value.Substring(i + 1, close - i - 1);
                sb.Append(ai);
                i = close + 1;

                // Copy the AI's data up to the next '('.
                int next = value.IndexOf('(', i);
                int end = next < 0 ? value.Length : next;
                string data = value[i..end];
                sb.Append(data);
                i = end;

                // Variable-length AIs need a trailing FNC1 unless at the very end.
                if (i < value.Length && IsVariableLengthAi(ai))
                {
                    sb.Append(Code128Encoder.Fnc1);
                }
            }
            else
            {
                sb.Append(value[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static bool IsVariableLengthAi(string ai)
    {
        // Fixed-length AIs per the GS1 General Specifications (common subset).
        string[] fixedAis =
        {
            "00", "01", "02", "03", "04", "11", "12", "13", "14", "15", "16", "17", "18", "19",
            "20", "31", "32", "33", "34", "35", "36", "41",
        };
        string prefix = ai.Length >= 2 ? ai[..2] : ai;
        return !fixedAis.Contains(prefix);
    }

    private static bool RunSelfTest(BarcodeSymbology symbology)
    {
        if (!SelfTestSamples.TryGetValue(symbology, out string? sample))
        {
            return false;
        }

        try
        {
            var spec = new BarcodeSpec { Symbology = symbology, Data = sample, Gs1Mode = symbology == BarcodeSymbology.GS1_128 };
            BarcodeSymbol symbol = Encode(spec, sample);
            return symbol.Is2D ? symbol.MatrixRows > 0 : symbol.TotalWidthModules > 0;
        }
        catch
        {
            return false;
        }
    }
}
