using System.Globalization;
using System.Text;

namespace LabelMaker.Core.Barcodes.Gs1;

/// <summary>What the data of an Application Identifier may contain.</summary>
public enum Gs1Charset
{
    /// <summary>Digits only.</summary>
    Numeric = 0,

    /// <summary>GS1 character set 82: letters, digits and <c>!"%&amp;'()*+,-./:;&lt;=&gt;?_</c>.</summary>
    Alphanumeric = 1,
}

/// <summary>How a value is checked beyond its length and characters.</summary>
public enum Gs1ValueKind
{
    Plain = 0,

    /// <summary>The last digit is a GS1 check digit (GTIN, SSCC, GLN…).</summary>
    CheckDigit = 1,

    /// <summary>A date as YYMMDD; day 00 means "end of the month".</summary>
    Date = 2,

    /// <summary>Date and time as YYMMDDHHMM.</summary>
    DateTime = 3,

    /// <summary>The first three digits are an ISO country or currency code, then the value.</summary>
    IsoCodeThenValue = 4,
}

/// <summary>
/// One GS1 Application Identifier: the number in brackets that says what the data after it
/// is, e.g. (17) for an expiry date. For the weight and price families the last digit of the
/// AI (shown as "n", as in 310n) is where the decimal point goes.
/// </summary>
public sealed record Gs1Ai(
    string Code,
    string Title,
    Gs1Charset Charset,
    int MinLength,
    int MaxLength,
    Gs1ValueKind Kind = Gs1ValueKind.Plain,
    string Hint = "")
{
    /// <summary>"310n": the AI's last digit is the number of decimals (0–5 or 0–9).</summary>
    public bool HasDecimalDigit => Code.EndsWith('n');

    public bool IsFixedLength => MinLength == MaxLength;

    /// <summary>The AI as written in a barcode, e.g. "3103" for 310n with 3 decimals.</summary>
    public string Written(int decimals = 0) => HasDecimalDigit ? Code[..^1] + decimals.ToString(CultureInfo.InvariantCulture) : Code;

    /// <summary>"(17) Expiry date", for lists.</summary>
    public string Display => $"({Code}) {Title}";
}

/// <summary>An Application Identifier and its data, e.g. (17) 261231.</summary>
public sealed record Gs1Element(string Ai, string Value);

/// <summary>Something wrong with GS1 data; <see cref="Index"/> is the element it is about (-1: the whole).</summary>
public sealed record Gs1Problem(int Index, string Message, bool IsError);

/// <summary>
/// GS1 barcode data: the Application Identifiers in common use, check digits, dates, the
/// rules about which AIs belong together, and the forms the data takes — "(01)…(17)…" as
/// typed and shown under the barcode, and the element string with group separators that
/// goes inside a GS1 DataMatrix or QR code.
/// </summary>
public static class Gs1
{
    /// <summary>The group separator (ASCII 29) that ends a variable-length element in an element string.</summary>
    public const char GroupSeparator = '\u001d';

    private const string Cset82 = "!\"%&'()*+,-./:;<=>?_";

    /// <summary>The Application Identifiers offered, in a sensible order.</summary>
    public static IReadOnlyList<Gs1Ai> All { get; } = new Gs1Ai[]
    {
        new("01", "Product number (GTIN)", Gs1Charset.Numeric, 14, 14, Gs1ValueKind.CheckDigit, "14 digits, the last a check digit"),
        new("02", "GTIN of the products inside", Gs1Charset.Numeric, 14, 14, Gs1ValueKind.CheckDigit, "14 digits, the last a check digit"),
        new("00", "Shipping container code (SSCC)", Gs1Charset.Numeric, 18, 18, Gs1ValueKind.CheckDigit, "18 digits, the last a check digit"),
        new("10", "Batch or lot number", Gs1Charset.Alphanumeric, 1, 20),
        new("21", "Serial number", Gs1Charset.Alphanumeric, 1, 20),
        new("11", "Production date", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Date, "YYMMDD"),
        new("13", "Packaging date", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Date, "YYMMDD"),
        new("15", "Best before date", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Date, "YYMMDD"),
        new("16", "Sell by date", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Date, "YYMMDD"),
        new("17", "Expiry date", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Date, "YYMMDD"),
        new("12", "Due date", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Date, "YYMMDD"),
        new("20", "Variant", Gs1Charset.Numeric, 2, 2),
        new("30", "Count of items", Gs1Charset.Numeric, 1, 8),
        new("37", "Count of trade items inside", Gs1Charset.Numeric, 1, 8),
        new("310n", "Net weight, kg", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Plain, "6 digits; n = digits after the decimal point"),
        new("320n", "Net weight, pounds", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Plain, "6 digits; n = digits after the decimal point"),
        new("330n", "Gross weight, kg", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Plain, "6 digits; n = digits after the decimal point"),
        new("311n", "Length, metres", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Plain, "6 digits; n = digits after the decimal point"),
        new("315n", "Net volume, litres", Gs1Charset.Numeric, 6, 6, Gs1ValueKind.Plain, "6 digits; n = digits after the decimal point"),
        new("390n", "Amount payable, local currency", Gs1Charset.Numeric, 1, 15, Gs1ValueKind.Plain, "n = digits after the decimal point"),
        new("392n", "Price, local currency", Gs1Charset.Numeric, 1, 15, Gs1ValueKind.Plain, "n = digits after the decimal point"),
        new("393n", "Price with ISO currency code", Gs1Charset.Numeric, 4, 18, Gs1ValueKind.IsoCodeThenValue, "3-digit currency code, then the price"),
        new("400", "Customer's order number", Gs1Charset.Alphanumeric, 1, 30),
        new("401", "Consignment number (GINC)", Gs1Charset.Alphanumeric, 1, 30),
        new("402", "Shipment number (GSIN)", Gs1Charset.Numeric, 17, 17, Gs1ValueKind.CheckDigit, "17 digits, the last a check digit"),
        new("410", "Ship to location (GLN)", Gs1Charset.Numeric, 13, 13, Gs1ValueKind.CheckDigit, "13 digits, the last a check digit"),
        new("414", "Location number (GLN)", Gs1Charset.Numeric, 13, 13, Gs1ValueKind.CheckDigit, "13 digits, the last a check digit"),
        new("420", "Ship to postal code", Gs1Charset.Alphanumeric, 1, 20),
        new("422", "Country of origin", Gs1Charset.Numeric, 3, 3, Gs1ValueKind.Plain, "3-digit ISO country code"),
        new("240", "Additional product number", Gs1Charset.Alphanumeric, 1, 30),
        new("241", "Customer part number", Gs1Charset.Alphanumeric, 1, 30),
        new("250", "Secondary serial number", Gs1Charset.Alphanumeric, 1, 30),
        new("7003", "Expiry date and time", Gs1Charset.Numeric, 10, 10, Gs1ValueKind.DateTime, "YYMMDDHHMM"),
        new("7240", "Protocol ID", Gs1Charset.Alphanumeric, 1, 20),
        new("8008", "Production date and time", Gs1Charset.Numeric, 8, 12, Gs1ValueKind.Plain, "YYMMDDHH, then minutes and seconds if wanted"),
        new("8020", "Payment slip reference", Gs1Charset.Alphanumeric, 1, 25),
        new("8200", "Product web address", Gs1Charset.Alphanumeric, 1, 70),
        new("90", "Agreed between trading partners", Gs1Charset.Alphanumeric, 1, 30),
        new("91", "Company internal information", Gs1Charset.Alphanumeric, 1, 90),
        new("92", "Company internal information (2)", Gs1Charset.Alphanumeric, 1, 90),
    };

    /// <summary>
    /// AIs whose first two digits fix their length, so no separator is needed after them
    /// (GS1 General Specifications, the table of predefined lengths).
    /// </summary>
    private static readonly HashSet<string> PredefinedLength = new(StringComparer.Ordinal)
    {
        "00", "01", "02", "03", "04", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
        "31", "32", "33", "34", "35", "36", "41",
    };

    /// <summary>The AI for a code as written in data ("3103" finds 310n), or null.</summary>
    public static Gs1Ai? Find(string? ai)
    {
        if (string.IsNullOrEmpty(ai))
        {
            return null;
        }

        return All.FirstOrDefault(a => a.Code == ai) ??
               All.FirstOrDefault(a => a.HasDecimalDigit && ai.Length == a.Code.Length && ai.StartsWith(a.Code[..^1], StringComparison.Ordinal) &&
                                       char.IsAsciiDigit(ai[^1]));
    }

    /// <summary>Whether the separator after this AI's data can be left out (its length is set by the standard).</summary>
    public static bool IsPredefinedLength(string ai) => ai.Length >= 2 && PredefinedLength.Contains(ai[..2]);

    // ---- check digits --------------------------------------------------------------------

    /// <summary>The GS1 check digit for <paramref name="digits"/> (the number without its check digit).</summary>
    public static int CheckDigit(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            // Weights 3, 1, 3, 1… from the right-hand digit.
            int d = digits[digits.Length - 1 - i] - '0';
            sum += d * (i % 2 == 0 ? 3 : 1);
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>Whether the last digit of <paramref name="number"/> is its correct GS1 check digit.</summary>
    public static bool HasValidCheckDigit(string number) =>
        number.Length >= 2 && number.All(char.IsAsciiDigit) && CheckDigit(number[..^1]) == number[^1] - '0';

    /// <summary>
    /// A product number made ready for (01): GTIN-8, -12 or -13 are padded to 14 digits with
    /// zeros in front. Returns null when it is not 8, 12, 13 or 14 digits.
    /// </summary>
    public static string? AsGtin14(string? number)
    {
        string digits = new((number ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return digits.All(char.IsAsciiDigit) && digits.Length is 8 or 12 or 13 or 14 ? digits.PadLeft(14, '0') : null;
    }

    // ---- dates ---------------------------------------------------------------------------

    /// <summary>A date as GS1 writes it: YYMMDD.</summary>
    public static string Date(DateTime date) => date.ToString("yyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The date a YYMMDD value stands for. Day 00 is the last day of the month. The century
    /// follows GS1: years up to 50 ahead are this century, the rest the last.
    /// </summary>
    public static DateTime? ReadDate(string? yymmdd, DateTime? today = null)
    {
        if (yymmdd is not { Length: 6 } || !yymmdd.All(char.IsAsciiDigit))
        {
            return null;
        }

        int yy = int.Parse(yymmdd[..2], CultureInfo.InvariantCulture);
        int mm = int.Parse(yymmdd.Substring(2, 2), CultureInfo.InvariantCulture);
        int dd = int.Parse(yymmdd.Substring(4, 2), CultureInfo.InvariantCulture);
        int thisYear = (today ?? DateTime.Today).Year;
        int century = thisYear / 100 * 100;
        int year = century + yy;
        if (year - thisYear > 50)
        {
            year -= 100;
        }
        else if (thisYear - year >= 50)
        {
            year += 100;
        }

        if (mm is < 1 or > 12)
        {
            return null;
        }

        int last = DateTime.DaysInMonth(year, mm);
        if (dd == 0)
        {
            dd = last;
        }

        return dd <= last ? new DateTime(year, mm, dd) : null;
    }

    /// <summary>
    /// Reads a date typed in a friendlier way — 31/12/2026, 2026-12-31, 31.12.26 — as YYMMDD.
    /// Already-YYMMDD values come back unchanged; anything else is null.
    /// </summary>
    public static string? ToYymmdd(string? text)
    {
        string t = (text ?? string.Empty).Trim();
        if (t.Length == 6 && t.All(char.IsAsciiDigit))
        {
            return t;
        }

        string[] formats =
        {
            "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "d.M.yyyy",
            "dd/MM/yy", "d/M/yy", "dd.MM.yy", "yyyyMMdd",
        };
        return System.DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? Date(date)
            : null;
    }

    // ---- the forms of GS1 data -----------------------------------------------------------

    /// <summary>The elements of data written as "(01)09506000134352(17)261231(10)ABC". Text outside brackets is ignored.</summary>
    public static List<Gs1Element> Parse(string? text)
    {
        var elements = new List<Gs1Element>();
        string s = (text ?? string.Empty).Replace(GroupSeparator.ToString(), string.Empty, StringComparison.Ordinal);
        int i = s.IndexOf('(');
        while (i >= 0 && i < s.Length)
        {
            int close = s.IndexOf(')', i);
            if (close < 0)
            {
                break;
            }

            string ai = s.Substring(i + 1, close - i - 1).Trim();
            int next = NextAiStart(s, close + 1);
            elements.Add(new Gs1Element(ai, s[(close + 1)..next].Trim()));
            i = next;
        }

        return elements;
    }

    // The next "(digits)" after position: brackets inside a value that are not an AI stay in the value.
    private static int NextAiStart(string s, int from)
    {
        for (int i = s.IndexOf('(', from); i >= 0; i = s.IndexOf('(', i + 1))
        {
            int close = s.IndexOf(')', i);
            if (close > i + 2 && close - i - 1 <= 4 && s.AsSpan(i + 1, close - i - 1).IndexOfAnyExceptInRange('0', '9') < 0)
            {
                return i;
            }
        }

        return s.Length;
    }

    /// <summary>"(01)09506000134352(17)261231(10)ABC": how GS1 data is typed in LabelAxis.</summary>
    public static string Format(IEnumerable<Gs1Element> elements) =>
        string.Concat(elements.Select(e => $"({e.Ai}){e.Value}"));

    /// <summary>"(01) 09506000134352 (17) 261231 (10) ABC": the text printed under a GS1 barcode.</summary>
    public static string HumanReadable(IEnumerable<Gs1Element> elements) =>
        string.Join(" ", elements.Select(e => $"({e.Ai}) {e.Value}"));

    /// <summary>
    /// The element string a GS1 DataMatrix or QR code holds: AIs and data run together, with
    /// a group separator after each variable-length element that is not the last.
    /// </summary>
    public static string ElementString(IReadOnlyList<Gs1Element> elements)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < elements.Count; i++)
        {
            sb.Append(elements[i].Ai).Append(elements[i].Value);
            if (i < elements.Count - 1 && !IsPredefinedLength(elements[i].Ai))
            {
                sb.Append(GroupSeparator);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The same elements in the order GS1 recommends: those of fixed length first, so that
    /// fewer separators are needed and the barcode is shorter. Otherwise the order is kept.
    /// </summary>
    public static List<Gs1Element> RecommendedOrder(IEnumerable<Gs1Element> elements) =>
        elements.Select((e, i) => (e, i))
            .OrderBy(x => IsPredefinedLength(x.e.Ai) ? 0 : 1)
            .ThenBy(x => x.i)
            .Select(x => x.e)
            .ToList();

    // ---- checking ------------------------------------------------------------------------

    /// <summary>Whether a value is to be filled in from data when printing ("{Expiry}").</summary>
    public static bool IsFromData(string value) => value.Contains('{') && value.Contains('}');

    /// <summary>
    /// Everything wrong with the elements: unknown AIs, wrong lengths or characters, check
    /// digits, dates, and AIs that are repeated or need another one with them. Values taken
    /// from data ({Field}) are only checked once they are filled in.
    /// </summary>
    public static List<Gs1Problem> Check(IReadOnlyList<Gs1Element> elements, DateTime? today = null)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var problems = new List<Gs1Problem>();
        if (elements.Count == 0)
        {
            problems.Add(new Gs1Problem(-1, "Add at least one piece of data, such as (01) the product number.", true));
            return problems;
        }

        for (int i = 0; i < elements.Count; i++)
        {
            CheckElement(i, elements[i], problems, today);
        }

        var codes = elements.Select(e => e.Ai).ToList();
        foreach (string repeated in codes.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            problems.Add(new Gs1Problem(codes.LastIndexOf(repeated), $"({repeated}) is used more than once.", true));
        }

        bool Has(params string[] ais) => codes.Any(c => ais.Any(a => c == a || (a.EndsWith('n') && c.Length == a.Length && c.StartsWith(a[..^1], StringComparison.Ordinal))));
        if (Has("01") && Has("02"))
        {
            problems.Add(new Gs1Problem(codes.IndexOf("02"), "(01) and (02) cannot be in the same barcode: (02) is for the products inside a carton that has no (01) of its own.", true));
        }

        if (Has("02") && !Has("37"))
        {
            problems.Add(new Gs1Problem(codes.IndexOf("02"), "(02) needs (37), the number of items inside, with it.", true));
        }

        if (Has("37") && !Has("02"))
        {
            problems.Add(new Gs1Problem(codes.IndexOf("37"), "(37) needs (02), the product number of the items inside, with it.", true));
        }

        // Batch, serial, dates, weights and counts describe a product, so they need its number.
        string[] needProduct = { "10", "11", "12", "13", "15", "16", "17", "20", "21", "30", "240", "250", "310n", "320n", "330n", "311n", "315n", "392n", "393n", "7003" };
        for (int i = 0; i < codes.Count; i++)
        {
            if (needProduct.Any(a => Matches(codes[i], a)) && !Has("01", "02"))
            {
                problems.Add(new Gs1Problem(i, $"({codes[i]}) needs (01), the product number, in the same barcode.", true));
                break;
            }
        }

        if (Has("21") && Has("02"))
        {
            problems.Add(new Gs1Problem(codes.IndexOf("21"), "(21) serial numbers go with (01), not (02).", true));
        }

        if (Has("390n") && !Has("8020") && !Has("01"))
        {
            problems.Add(new Gs1Problem(codes.FindIndex(c => Matches(c, "390n")), "(390n) needs (8020) or (01) with it.", false));
        }

        return problems;
    }

    private static bool Matches(string code, string ai) =>
        code == ai || (ai.EndsWith('n') && code.Length == ai.Length && code.StartsWith(ai[..^1], StringComparison.Ordinal));

    private static void CheckElement(int index, Gs1Element element, List<Gs1Problem> problems, DateTime? today)
    {
        string ai = element.Ai, value = element.Value;
        if (ai.Length is < 2 or > 4 || !ai.All(char.IsAsciiDigit))
        {
            problems.Add(new Gs1Problem(index, $"“{ai}” is not an Application Identifier: they are 2 to 4 digits.", true));
            return;
        }

        Gs1Ai? info = Find(ai);
        if (value.Length == 0)
        {
            problems.Add(new Gs1Problem(index, $"({ai}) has no data.", true));
            return;
        }

        if (IsFromData(value))
        {
            return; // checked when printing, with the data filled in
        }

        if (info is null)
        {
            if (value.Any(c => !IsCset82(c)))
            {
                problems.Add(new Gs1Problem(index, $"({ai}) contains a character GS1 barcodes cannot hold.", true));
            }

            problems.Add(new Gs1Problem(index, $"({ai}) is not in LabelAxis's list; check its rules in the GS1 standard.", false));
            return;
        }

        if (info.Charset == Gs1Charset.Numeric && !value.All(char.IsAsciiDigit))
        {
            problems.Add(new Gs1Problem(index, $"({ai}) {info.Title} must be digits only.", true));
            return;
        }

        if (value.Any(c => !IsCset82(c)))
        {
            problems.Add(new Gs1Problem(index, $"({ai}) contains a character GS1 barcodes cannot hold (letters, digits and !\"%&'()*+,-./:;<=>?_ only).", true));
            return;
        }

        if (value.Length < info.MinLength || value.Length > info.MaxLength)
        {
            string wanted = info.IsFixedLength ? $"exactly {info.MinLength}" : $"{info.MinLength} to {info.MaxLength}";
            problems.Add(new Gs1Problem(index, $"({ai}) {info.Title} must be {wanted} characters long; it is {value.Length}.", true));
            return;
        }

        switch (info.Kind)
        {
            case Gs1ValueKind.CheckDigit when !HasValidCheckDigit(value):
                problems.Add(new Gs1Problem(index,
                    $"({ai}) {info.Title}: the last digit should be {CheckDigit(value[..^1])}, the check digit, not {value[^1]}.", true));
                break;
            case Gs1ValueKind.Date when ReadDate(value, today) is null:
                problems.Add(new Gs1Problem(index, $"({ai}) {info.Title}: {value} is not a real date (YYMMDD).", true));
                break;
            case Gs1ValueKind.Date when ai is "17" or "15" or "16" && ReadDate(value, today) is { } date && date < (today ?? DateTime.Today):
                problems.Add(new Gs1Problem(index, $"({ai}) {info.Title} {date:d MMM yyyy} has already passed.", false));
                break;
            case Gs1ValueKind.DateTime when ReadDate(value[..6], today) is null || int.Parse(value[6..8], CultureInfo.InvariantCulture) > 23 ||
                                            int.Parse(value[8..10], CultureInfo.InvariantCulture) > 59:
                problems.Add(new Gs1Problem(index, $"({ai}) {info.Title}: {value} is not a real date and time (YYMMDDHHMM).", true));
                break;
        }

        if (info.HasDecimalDigit && ai[^1] - '0' > (info.Code.StartsWith("39", StringComparison.Ordinal) ? 9 : 5))
        {
            problems.Add(new Gs1Problem(index, $"({ai}): the last digit of the AI is the number of decimals, at most 5.", true));
        }
    }

    private static bool IsCset82(char c) => char.IsAsciiLetterOrDigit(c) || Cset82.Contains(c);

    /// <summary>
    /// A value written in an AI's form where that is clear: dates typed as 31/12/2026 become
    /// 261231, a GTIN-13 gets its leading zero for (01). Anything else is returned as it was.
    /// </summary>
    public static string Tidy(string ai, string value)
    {
        string v = (value ?? string.Empty).Trim();
        if (IsFromData(v) || Find(ai) is not { } info)
        {
            return v;
        }

        if (info.Kind == Gs1ValueKind.Date && ToYymmdd(v) is { } date)
        {
            return date;
        }

        if (ai is "01" or "02" && AsGtin14(v) is { } gtin && HasValidCheckDigit(gtin))
        {
            return gtin;
        }

        return info.Charset == Gs1Charset.Numeric ? new string(v.Where(c => !char.IsWhiteSpace(c)).ToArray()) : v;
    }
}
