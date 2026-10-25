using System.Text;

namespace LabelMaker.Core.Localization;

/// <summary>
/// Digits as Persian, Kurdish, Arabic and Hindi keyboards type them (۱۲, ١٢, १२) turned into the
/// 0–9 that numbers are read with. Only for boxes that take a number: text on a label keeps the
/// digits it was typed with.
/// </summary>
public static class NativeDigits
{
    /// <summary><paramref name="text"/> with every native digit as 0–9 and the Arabic decimal mark as "."; null stays null.</summary>
    public static string? ToAscii(string? text)
    {
        if (string.IsNullOrEmpty(text) || !HasNative(text))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(Ascii(c));
        }

        return sb.ToString();
    }

    /// <summary>Whether <paramref name="text"/> has a digit or decimal mark that <see cref="ToAscii"/> changes.</summary>
    public static bool HasNative(string? text) => text is not null && text.Any(c => Ascii(c) != c);

    private static char Ascii(char c) => c switch
    {
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')), // Arabic-Indic
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')), // Extended Arabic-Indic (Persian, Kurdish)
        >= '०' and <= '९' => (char)('0' + (c - '०')), // Devanagari
        '٫' => '.', // Arabic decimal separator
        '٬' => ',', // Arabic thousands separator
        _ => c,
    };
}
