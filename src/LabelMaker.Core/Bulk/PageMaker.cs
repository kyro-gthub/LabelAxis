using System.Globalization;
using System.Text;
using LabelMaker.Core.Barcodes;
using LabelMaker.Core.Barcodes.Gs1;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Model.Text;
using LabelMaker.Core.Variables;

namespace LabelMaker.Core.Bulk;

/// <summary>
/// Standalone pages made from a label in one go: one page per data record, with that record's
/// values written into its text, barcodes and table cells (<see cref="ForRecord"/>); or numbered
/// copies whose barcodes count up page by page (<see cref="Numbered"/>). The new pages are
/// ordinary pages, edited and printed like any other.
/// </summary>
public static class PageMaker
{
    /// <summary>
    /// A copy of <paramref name="label"/> with <paramref name="record"/> written in. Only
    /// placeholders that use the record's own columns are filled in (<c>{Product}</c>,
    /// <c>{UPPER({Product})}</c>); dates, counters, page numbers and other variables stay
    /// placeholders, so they keep working when the page prints.
    /// </summary>
    public static LabelDocument ForRecord(LabelDocument label, IReadOnlyDictionary<string, string> record, IReadOnlyList<CustomVariable>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(record);
        var columns = new HashSet<string>(record.Keys, StringComparer.OrdinalIgnoreCase);
        var resolver = new VariableResolver(new ResolveContext { Record = new CaseInsensitive(record), Variables = variables ?? Array.Empty<CustomVariable>() });

        LabelDocument page = label.Clone();
        page.Id = Guid.NewGuid().ToString("N");
        foreach (LabelObject obj in page.Objects)
        {
            switch (obj)
            {
                case TextObject text:
                    foreach (TextRun run in text.Content.Paragraphs.SelectMany(p => p.Runs))
                    {
                        run.Text = FillFields(run.Text, columns, resolver);
                    }

                    break;
                case BarcodeObject barcode:
                    barcode.Spec.Data = FillFields(barcode.Spec.Data, columns, resolver);
                    break;
                case TableObject table:
                    for (int i = 0; i < table.Cells.Count; i++)
                    {
                        table.Cells[i] = FillFields(table.Cells[i], columns, resolver);
                    }

                    break;
            }
        }

        return page;
    }

    /// <summary><paramref name="text"/> with every <c>{…}</c> that uses one of <paramref name="columns"/> replaced by its value.</summary>
    public static string FillFields(string? text, IReadOnlySet<string> columns, VariableResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(resolver);
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{')
            {
                sb.Append(text[i++]);
                continue;
            }

            int end = MatchingBrace(text, i);
            if (end < 0)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }

            string token = text.Substring(i, end - i + 1);
            sb.Append(TokenScanner.ExtractFieldNames(token).Any(columns.Contains) ? resolver.Resolve(token) : token);
            i = end + 1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// <paramref name="count"/> copies of <paramref name="label"/>; copy <c>k</c> (from 1) has every
    /// barcode's number counted up by <c>k × step</c> (<see cref="CountUp"/>). Barcodes filled from
    /// data (<c>{…}</c>) or without a number are left as they are.
    /// </summary>
    public static IReadOnlyList<LabelDocument> Numbered(LabelDocument label, int count, int step = 1)
    {
        ArgumentNullException.ThrowIfNull(label);
        var pages = new List<LabelDocument>(Math.Max(0, count));
        for (int k = 1; k <= count; k++)
        {
            LabelDocument page = label.Clone();
            page.Id = Guid.NewGuid().ToString("N");
            foreach (BarcodeObject barcode in page.Objects.OfType<BarcodeObject>())
            {
                barcode.Spec.Data = CountUp(barcode.Spec.Data, barcode.Spec.Symbology, (long)k * step) ?? barcode.Spec.Data;
            }

            pages.Add(page);
        }

        return pages;
    }

    /// <summary>
    /// The barcode value <paramref name="by"/> numbers on: the last run of digits counted up with
    /// its zeros kept ("SKU-000123" → "SKU-000124"). For EAN, UPC, ITF-14 and ISBN given with
    /// their check digit, the number before it counts up and the check digit is worked out again
    /// (for UPC-E through the UPC-A number it stands for). GS1 data such as
    /// "(01)…(17)261231(21)0001" counts up its serial number (21), or else its SSCC (00) or a
    /// batch (10) ending in a number; dates, GTINs and the rest stay as they are. Null when there
    /// is no number to count, or the value is filled from data.
    /// </summary>
    public static string? CountUp(string? value, BarcodeSymbology symbology, long by)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf('{') >= 0)
        {
            return null;
        }

        if (value.TrimStart().StartsWith('(') && Gs1.Parse(value) is { Count: > 0 } elements)
        {
            return CountUpGs1(elements, by);
        }

        if (symbology == BarcodeSymbology.UpcE && value.Length == 8 && value.All(char.IsAsciiDigit))
        {
            string middle = Add(value[1..7], by);
            if (middle.Length != 6)
            {
                return null;
            }

            string body = value[0] + middle;
            return body + Gs1.CheckDigit(UpcEToUpcA(body)).ToString(CultureInfo.InvariantCulture);
        }

        int? full = symbology switch
        {
            BarcodeSymbology.Ean13 or BarcodeSymbology.Isbn13 => 13,
            BarcodeSymbology.Ean8 => 8,
            BarcodeSymbology.UpcA => 12,
            BarcodeSymbology.Itf14 => 14,
            _ => null,
        };
        if (full is { } length && value.Length == length && value.All(char.IsAsciiDigit))
        {
            string body = Add(value[..^1], by);
            return body.Length == length - 1 ? body + Gs1.CheckDigit(body).ToString(CultureInfo.InvariantCulture) : null;
        }

        int last = value.Length - 1;
        while (last >= 0 && !char.IsAsciiDigit(value[last]))
        {
            last--;
        }

        if (last < 0)
        {
            return null;
        }

        int first = last;
        while (first > 0 && char.IsAsciiDigit(value[first - 1]))
        {
            first--;
        }

        return value[..first] + Add(value[first..(last + 1)], by) + value[(last + 1)..];
    }

    // The serial number counts first; an SSCC (one per pallet or carton) or a numbered batch otherwise.
    private static readonly string[] CountingAis = { "21", "00", "10" };

    private static string? CountUpGs1(List<Gs1Element> elements, long by)
    {
        foreach (string ai in CountingAis)
        {
            int index = elements.FindIndex(e => e.Ai == ai);
            if (index < 0)
            {
                continue;
            }

            string v = elements[index].Value;
            string? counted;
            if (ai == "00")
            {
                // 17 digits and a check digit worked out again, as wide as before.
                if (v.Length != 18 || !v.All(char.IsAsciiDigit) || Add(v[..17], by) is not { Length: 17 } body)
                {
                    continue;
                }

                counted = body + Gs1.CheckDigit(body).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                int first = v.Length;
                while (first > 0 && char.IsAsciiDigit(v[first - 1]))
                {
                    first--;
                }

                if (first == v.Length)
                {
                    continue;
                }

                counted = v[..first] + Add(v[first..], by);
            }

            var copy = new List<Gs1Element>(elements) { [index] = elements[index] with { Value = counted } };
            return Gs1.Format(copy);
        }

        return null;
    }

    /// <summary>The 11-digit UPC-A number (without its check digit) a 7-digit UPC-E number stands for.</summary>
    private static string UpcEToUpcA(string upcE)
    {
        char ns = upcE[0];
        string d = upcE[1..7];
        return d[5] switch
        {
            '0' or '1' or '2' => $"{ns}{d[0]}{d[1]}{d[5]}0000{d[2]}{d[3]}{d[4]}",
            '3' => $"{ns}{d[0]}{d[1]}{d[2]}00000{d[3]}{d[4]}",
            '4' => $"{ns}{d[0]}{d[1]}{d[2]}{d[3]}00000{d[4]}",
            _ => $"{ns}{d[0]}{d[1]}{d[2]}{d[3]}{d[4]}0000{d[5]}",
        };
    }

    /// <summary>The digits <paramref name="digits"/> plus <paramref name="by"/>, as wide as before (or wider when it runs over).</summary>
    private static string Add(string digits, long by)
    {
        var sum = System.Numerics.BigInteger.Parse(digits, CultureInfo.InvariantCulture) + by;
        string text = System.Numerics.BigInteger.Max(sum, 0).ToString(CultureInfo.InvariantCulture);
        return text.Length < digits.Length ? text.PadLeft(digits.Length, '0') : text;
    }

    private static int MatchingBrace(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A record looked up by column name in any letter case, as <c>{product}</c> finds "Product".</summary>
    private sealed class CaseInsensitive : IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, string> _values;

        public CaseInsensitive(IReadOnlyDictionary<string, string> record)
        {
            _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, string value) in record)
            {
                _values.TryAdd(key, value);
            }
        }

        public string this[string key] => _values[key];

        public IEnumerable<string> Keys => _values.Keys;

        public IEnumerable<string> Values => _values.Values;

        public int Count => _values.Count;

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
