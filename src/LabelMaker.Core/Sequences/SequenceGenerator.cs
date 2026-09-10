using System.Globalization;

namespace LabelMaker.Core.Sequences;

/// <summary>Turns a <see cref="SequenceDefinition"/> into the actual string values.</summary>
public static class SequenceGenerator
{
    /// <summary>Lazily enumerate every value in the series (safe for millions of items).</summary>
    public static IEnumerable<string> Enumerate(SequenceDefinition def)
    {
        ArgumentNullException.ThrowIfNull(def);
        if (def.Step == 0)
        {
            throw new InvalidOperationException("Sequence step cannot be zero.");
        }

        long count = def.EffectiveCount;
        long value = def.Start;
        for (long i = 0; i < count; i++)
        {
            yield return Render(def, value);
            value += def.Step;
        }
    }

    /// <summary>Materialise the whole series. Guards against runaway sizes.</summary>
    public static IReadOnlyList<string> Generate(SequenceDefinition def, long safetyLimit = 5_000_000)
    {
        long count = def.EffectiveCount;
        if (count > safetyLimit)
        {
            throw new InvalidOperationException(
                $"Sequence would produce {count:N0} values, above the safety limit of {safetyLimit:N0}.");
        }

        var list = new List<string>((int)Math.Min(count, int.MaxValue));
        list.AddRange(Enumerate(def));
        return list;
    }

    public static string Render(SequenceDefinition def, long value)
    {
        string number = def.FormatNumber(value);
        string result;

        if (!string.IsNullOrEmpty(def.Pattern))
        {
            result = ApplyPattern(def.Pattern!, number);
        }
        else
        {
            result = def.Prefix + number + def.Suffix;
        }

        return def.Casing switch
        {
            SequenceCasing.Upper => result.ToUpperInvariantSafe(),
            SequenceCasing.Lower => result.ToLowerInvariant(),
            _ => result,
        };
    }

    /// <summary>First and last rendered values, for the preview shown before generation (spec §8).</summary>
    public static (string First, string Last, long Count) Preview(SequenceDefinition def)
    {
        long count = def.EffectiveCount;
        if (count <= 0)
        {
            return (string.Empty, string.Empty, 0);
        }

        long last = def.Start + (def.Step * (count - 1));
        return (Render(def, def.Start), Render(def, last), count);
    }

    private static string ApplyPattern(string pattern, string number)
    {
        int start = pattern.IndexOf('#');
        if (start < 0)
        {
            return pattern; // no placeholder — treat pattern as a literal
        }

        int end = start;
        while (end < pattern.Length && pattern[end] == '#')
        {
            end++;
        }

        return string.Concat(pattern.AsSpan(0, start), number, pattern.AsSpan(end));
    }

    private static string ToUpperInvariantSafe(this string s) => s.ToUpper(CultureInfo.InvariantCulture);
}
