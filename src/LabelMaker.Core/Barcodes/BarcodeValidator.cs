using System.Globalization;

namespace LabelMaker.Core.Barcodes;

public enum BarcodeIssueSeverity
{
    Error = 0,
    Warning = 1,
}

public readonly record struct BarcodeIssue(BarcodeIssueSeverity Severity, string Message);

public sealed class BarcodeValidationResult
{
    public List<BarcodeIssue> Issues { get; } = new();

    public bool IsValid => Issues.All(i => i.Severity != BarcodeIssueSeverity.Error);

    public bool HasWarnings => Issues.Any(i => i.Severity == BarcodeIssueSeverity.Warning);

    internal void Error(string m) => Issues.Add(new BarcodeIssue(BarcodeIssueSeverity.Error, m));

    internal void Warn(string m) => Issues.Add(new BarcodeIssue(BarcodeIssueSeverity.Warning, m));
}

/// <summary>
/// Pre-encode data checks so preflight (spec §26) and the designer can flag a bad
/// value before it ever reaches a printer. Deep structural validation still
/// happens inside the encoders; this catches the common, explainable mistakes.
/// </summary>
public static class BarcodeValidator
{
    public static BarcodeValidationResult Validate(BarcodeSpec spec, string resolvedValue)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var r = new BarcodeValidationResult();
        string value = resolvedValue ?? string.Empty;

        if (value.Length == 0 && !spec.Symbology.Is2D())
        {
            r.Error("Barcode value is empty.");
            return r;
        }

        switch (spec.Symbology)
        {
            case BarcodeSymbology.Code128A:
                RequireAscii(r, value, 0, 95, "Code 128 Set A");
                break;
            case BarcodeSymbology.Code128B:
                RequireAscii(r, value, 32, 127, "Code 128 Set B");
                break;
            case BarcodeSymbology.Code128C:
                if (StripFnc(value) is var digitsOnly && (digitsOnly.Length % 2 != 0 || !digitsOnly.All(char.IsDigit)))
                {
                    r.Error("Code 128 Set C needs an even number of digits.");
                }

                break;
            case BarcodeSymbology.Code128:
            case BarcodeSymbology.GS1_128:
                foreach (char c in StripFnc(value))
                {
                    if (c > 127)
                    {
                        r.Error($"Code 128 cannot encode U+{(int)c:X4}; only ASCII 0-127 is allowed.");
                        break;
                    }
                }

                break;

            case BarcodeSymbology.Ean13:
            case BarcodeSymbology.Isbn13:
                ValidateFixedNumeric(r, value, 13, allowCheckDigitOmitted: true, "EAN-13 / ISBN-13");
                break;
            case BarcodeSymbology.Ean8:
                ValidateFixedNumeric(r, value, 8, allowCheckDigitOmitted: true, "EAN-8");
                break;
            case BarcodeSymbology.UpcA:
                ValidateFixedNumeric(r, value, 12, allowCheckDigitOmitted: true, "UPC-A");
                break;
            case BarcodeSymbology.UpcE:
                ValidateFixedNumeric(r, value, 8, allowCheckDigitOmitted: true, "UPC-E");
                break;

            case BarcodeSymbology.Itf14:
                if (!value.All(char.IsDigit) || (value.Length != 13 && value.Length != 14))
                {
                    r.Error("ITF-14 expects 13 or 14 digits.");
                }

                break;
            case BarcodeSymbology.Interleaved2of5:
                if (!value.All(char.IsDigit))
                {
                    r.Error("Interleaved 2 of 5 accepts digits only.");
                }
                else if (value.Length % 2 != 0 && !spec.AddCheckDigit)
                {
                    r.Warn("Interleaved 2 of 5 has an odd digit count; a leading zero or a check digit will be added.");
                }

                break;

            case BarcodeSymbology.Code39:
                foreach (char c in value)
                {
                    if (!Code39Alphabet.Contains(char.ToUpperInvariant(c)))
                    {
                        r.Error($"Code 39 cannot encode '{c}'. Allowed: A-Z 0-9 - . space $ / + %.");
                        break;
                    }
                }

                if (value.Any(char.IsLower))
                {
                    r.Warn("Code 39 is upper-case only; lower-case letters will be upper-cased.");
                }

                break;

            case BarcodeSymbology.Codabar:
                if (value.Length > 0 && !"ABCD".Contains(char.ToUpperInvariant(value[0])))
                {
                    r.Warn("Codabar usually starts and ends with a guard letter A-D.");
                }

                foreach (char c in value)
                {
                    if (!"0123456789-$:/.+ABCD".Contains(char.ToUpperInvariant(c)))
                    {
                        r.Error($"Codabar cannot encode '{c}'.");
                        break;
                    }
                }

                break;

            case BarcodeSymbology.MsiPlessey:
                if (!value.All(char.IsDigit))
                {
                    r.Error("MSI Plessey accepts digits only.");
                }

                break;

            case BarcodeSymbology.QrCode:
                EstimateQrCapacity(r, spec, value);
                break;
            case BarcodeSymbology.DataMatrix:
                if (value.Length > 2335)
                {
                    r.Error("Data Matrix payload exceeds its maximum capacity.");
                }

                break;
            case BarcodeSymbology.Pdf417:
                if (value.Length > 1850)
                {
                    r.Error("PDF417 text payload exceeds ~1850 characters.");
                }

                break;
            case BarcodeSymbology.Aztec:
                if (value.Length > 3000)
                {
                    r.Warn("Aztec payload is very large; the symbol may be hard to scan at label size.");
                }

                break;
        }

        if (spec.Gs1Mode || spec.Symbology == BarcodeSymbology.GS1_128)
        {
            ValidateGs1(r, value);
        }

        if (spec.ModuleWidthMm < 0.19)
        {
            r.Warn($"Module width {spec.ModuleWidthMm:0.###} mm is below ~7.5 mil; many scanners will struggle.");
        }

        if (!spec.Symbology.Is2D() && spec.BarHeightMm < 5.0)
        {
            r.Warn($"Bar height {spec.BarHeightMm:0.#} mm is short; a taller bar scans more reliably.");
        }

        return r;
    }

    private const string Code39Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-. $/+%";

    private static string StripFnc(string s) =>
        new(s.Where(c => c is not ('ñ' or 'ò' or 'ó' or 'ô')).ToArray());

    private static void RequireAscii(BarcodeValidationResult r, string value, int lo, int hi, string label)
    {
        foreach (char c in StripFnc(value))
        {
            if (c < lo || c > hi)
            {
                r.Error($"{label} cannot encode U+{(int)c:X4}.");
                return;
            }
        }
    }

    private static void ValidateFixedNumeric(
        BarcodeValidationResult r, string value, int fullLength, bool allowCheckDigitOmitted, string label)
    {
        if (!value.All(char.IsDigit))
        {
            r.Error($"{label} accepts digits only.");
            return;
        }

        if (value.Length == fullLength)
        {
            int expected = GtinCheckDigit(value[..^1]);
            if (expected != value[^1] - '0')
            {
                r.Error($"{label} check digit is {value[^1]}, expected {expected}.");
            }

            return;
        }

        if (allowCheckDigitOmitted && value.Length == fullLength - 1)
        {
            r.Warn($"{label} check digit will be calculated automatically.");
            return;
        }

        r.Error($"{label} expects {fullLength} digits (or {fullLength - 1} without the check digit).");
    }

    private static int GtinCheckDigit(string digitsWithoutCheck)
    {
        int sum = 0;
        for (int i = 0; i < digitsWithoutCheck.Length; i++)
        {
            int d = digitsWithoutCheck[^(i + 1)] - '0';
            sum += i % 2 == 0 ? d * 3 : d;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static void ValidateGs1(BarcodeValidationResult r, string value)
    {
        string s = StripFnc(value);
        if (!s.StartsWith('(') && s.Length > 0)
        {
            r.Warn("GS1 data does not start with an Application Identifier in parentheses, e.g. (01).");
            return;
        }

        int i = 0;
        while (i < s.Length)
        {
            if (s[i] != '(')
            {
                break;
            }

            int close = s.IndexOf(')', i);
            if (close < 0)
            {
                r.Error("Unclosed '(' in GS1 Application Identifier.");
                return;
            }

            string ai = s.Substring(i + 1, close - i - 1);
            if (ai.Length is < 2 or > 4 || !ai.All(char.IsDigit))
            {
                r.Error($"'({ai})' is not a valid GS1 Application Identifier.");
                return;
            }

            i = close + 1;
            while (i < s.Length && s[i] != '(')
            {
                i++;
            }
        }
    }

    private static void EstimateQrCapacity(BarcodeValidationResult r, BarcodeSpec spec, string value)
    {
        // Version 40 numeric/alnum/byte ceilings by EC level (upper bounds only).
        int byteCap = spec.QrEcLevel switch
        {
            QrErrorCorrection.Low => 2953,
            QrErrorCorrection.Medium => 2331,
            QrErrorCorrection.Quartile => 1663,
            _ => 1273,
        };

        int bytes = System.Text.Encoding.UTF8.GetByteCount(value);
        if (bytes > byteCap)
        {
            r.Error($"QR payload ({bytes} bytes) exceeds capacity {byteCap} at EC level {spec.QrEcLevel}. " +
                    "Lower the EC level or shorten the data.");
        }
        else if (bytes > byteCap * 0.9)
        {
            r.Warn("QR payload is near capacity; the symbol will be dense and require a clean print.");
        }
    }
}
