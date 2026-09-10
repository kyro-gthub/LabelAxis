using System.Globalization;

namespace LabelMaker.Core.Sequences;

public enum SequenceCasing
{
    AsIs = 0,
    Upper = 1,
    Lower = 2,
}

/// <summary>
/// Declarative description of a running number / code series (spec §8). Pure
/// data; <see cref="SequenceGenerator"/> turns it into values.
/// </summary>
public sealed class SequenceDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Sequence";

    /// <summary>Text before the number, e.g. "PRD-".</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Text after the number, e.g. "-IRQ".</summary>
    public string Suffix { get; set; } = string.Empty;

    public long Start { get; set; } = 1;

    /// <summary>Inclusive end. Ignored when <see cref="UseCount"/> is true.</summary>
    public long End { get; set; } = 100;

    public long Step { get; set; } = 1;

    /// <summary>When true, generate exactly <see cref="Count"/> values from <see cref="Start"/>.</summary>
    public bool UseCount { get; set; }

    public long Count { get; set; } = 100;

    /// <summary>Zero-pad the numeric portion to this width (0 = no padding).</summary>
    public int Padding { get; set; } = 6;

    /// <summary>
    /// Optional radix for the numeric portion (10 = decimal, 16 = hex, 36 =
    /// base-36 alphanumeric). Padding still applies to the rendered digits.
    /// </summary>
    public int Radix { get; set; } = 10;

    public SequenceCasing Casing { get; set; } = SequenceCasing.Upper;

    /// <summary>
    /// Optional pattern with a single <c>#</c> run marking where the number goes,
    /// e.g. <c>BOX-A-#####</c> or <c>ITEM2026-######</c>. When set it overrides
    /// <see cref="Prefix"/>/<see cref="Suffix"/>/<see cref="Padding"/>.
    /// </summary>
    public string? Pattern { get; set; }

    public long EffectiveCount
    {
        get
        {
            if (Step == 0)
            {
                return 0;
            }

            if (UseCount)
            {
                return Math.Max(0, Count);
            }

            long span = End - Start;
            if (Math.Sign(span) != Math.Sign(Step) && span != 0)
            {
                return 0;
            }

            return (Math.Abs(span) / Math.Abs(Step)) + 1;
        }
    }

    public SequenceDefinition Clone() => (SequenceDefinition)MemberwiseClone();

    internal string FormatNumber(long value)
    {
        string digits = Radix == 10
            ? Math.Abs(value).ToString(CultureInfo.InvariantCulture)
            : ToRadix(Math.Abs(value), Radix);

        int pad = Pattern is null ? Padding : CountHashes(Pattern);
        if (pad > 0)
        {
            digits = digits.PadLeft(pad, '0');
        }

        string sign = value < 0 ? "-" : string.Empty;
        return sign + digits;
    }

    internal static int CountHashes(string pattern)
    {
        int max = 0, run = 0;
        foreach (char c in pattern)
        {
            if (c == '#')
            {
                run++;
                max = Math.Max(max, run);
            }
            else
            {
                run = 0;
            }
        }

        return max;
    }

    private static string ToRadix(long value, int radix)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (radix is < 2 or > 36)
        {
            throw new ArgumentOutOfRangeException(nameof(radix), "Radix must be 2..36.");
        }

        if (value == 0)
        {
            return "0";
        }

        Span<char> buffer = stackalloc char[64];
        int i = buffer.Length;
        while (value > 0)
        {
            buffer[--i] = digits[(int)(value % radix)];
            value /= radix;
        }

        return new string(buffer[i..]);
    }
}
