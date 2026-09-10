using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Sequences;

namespace LabelMaker.Core.Bulk;

public enum BarcodeImageFormat
{
    Png = 0,
    Svg = 1,
    Pdf = 2,
    Jpeg = 3,
}

/// <summary>Options for <see cref="BulkBarcodeService"/> (spec §7, §34).</summary>
public sealed class BulkBarcodeRequest
{
    public required SequenceDefinition Sequence { get; init; }

    public BarcodeSpec Template { get; init; } = new();

    public BarcodeImageFormat Format { get; init; } = BarcodeImageFormat.Png;

    public int Dpi { get; init; } = 300;

    /// <summary>Optional fixed output size in millimetres (0 = natural size from module width).</summary>
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }

    public bool TransparentBackground { get; init; }

    public bool HumanReadable { get; init; } = true;

    /// <summary>
    /// File name pattern. <c>{value}</c> = barcode value, <c>{index}</c> = 1-based
    /// ordinal, <c>{index:0000}</c> = padded ordinal.
    /// </summary>
    public string FileNamePattern { get; init; } = "{value}";

    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>Abort if the series would exceed this many items.</summary>
    public int MaxItems { get; init; } = 200_000;
}

public sealed class BulkBarcodeItem
{
    public required int Ordinal { get; init; }          // 1-based
    public required string Value { get; init; }
    public required string FileName { get; init; }      // without directory, without extension
    public required BarcodeSpec Spec { get; init; }
}

/// <summary>
/// Expands a sequence into per-value barcode jobs. Rendering to actual image
/// bytes is delegated to a platform writer (SVG in <c>LabelMaker.Export</c>,
/// PNG/JPEG in the WPF app) so the core stays UI-free.
/// </summary>
public static class BulkBarcodeService
{
    public static (string First, string Last, long Count) Preview(BulkBarcodeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SequenceGenerator.Preview(request.Sequence);
    }

    public static IEnumerable<BulkBarcodeItem> Enumerate(BulkBarcodeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        long count = request.Sequence.EffectiveCount;
        if (count > request.MaxItems)
        {
            throw new InvalidOperationException(
                $"The series has {count:N0} items, above the configured limit of {request.MaxItems:N0}.");
        }

        int ordinal = 1;
        foreach (string value in SequenceGenerator.Enumerate(request.Sequence))
        {
            BarcodeSpec spec = request.Template.Clone();
            spec.Data = value;
            if (!request.HumanReadable)
            {
                spec.HumanReadable = HumanReadablePosition.None;
            }

            yield return new BulkBarcodeItem
            {
                Ordinal = ordinal,
                Value = value,
                FileName = SanitizeFileName(FormatName(request.FileNamePattern, value, ordinal)),
                Spec = spec,
            };

            ordinal++;
        }
    }

    /// <summary>Validate every value up front so a bad symbology fails before any file is written.</summary>
    public static IReadOnlyList<string> ValidateAll(BulkBarcodeRequest request)
    {
        var errors = new List<string>();
        foreach (BulkBarcodeItem item in Enumerate(request))
        {
            BarcodeValidationResult result = BarcodeValidator.Validate(item.Spec, item.Value);
            if (!result.IsValid)
            {
                errors.Add($"{item.Value}: {string.Join("; ", result.Issues.Where(i => i.Severity == BarcodeIssueSeverity.Error).Select(i => i.Message))}");
            }

            if (errors.Count >= 50)
            {
                errors.Add("… further errors suppressed.");
                break;
            }
        }

        return errors;
    }

    private static string FormatName(string pattern, string value, int ordinal)
    {
        string result = pattern
            .Replace("{value}", value, StringComparison.OrdinalIgnoreCase);

        int idx;
        while ((idx = result.IndexOf("{index", StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int close = result.IndexOf('}', idx);
            if (close < 0)
            {
                break;
            }

            string token = result.Substring(idx, close - idx + 1);
            string inner = token[1..^1];
            int colon = inner.IndexOf(':');
            string formatted = colon >= 0
                ? ordinal.ToString(inner[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture)
                : ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result = result.Replace(token, formatted);
        }

        return result;
    }

    private static string SanitizeFileName(string name)
    {
        Span<char> buffer = stackalloc char[name.Length == 0 ? 1 : name.Length];
        int n = 0;
        foreach (char c in name)
        {
            buffer[n++] = Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c;
        }

        string result = n == 0 ? "barcode" : new string(buffer[..n]);
        return result;
    }
}
