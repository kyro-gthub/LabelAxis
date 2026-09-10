using System.Text.RegularExpressions;

namespace LabelMaker.Core.Variables;

/// <summary>
/// Finds <c>{ ... }</c> placeholders in authored strings. The content between the
/// braces is either a bare field name (<c>{Barcode}</c>) or an expression
/// (<c>{UPPER({Product})}</c>) — this scanner only extracts the outermost
/// bracket spans and the plain field identifiers inside them.
/// </summary>
public static partial class TokenScanner
{
    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleToken();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_ ]*", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    /// <summary>True when the string contains at least one <c>{token}</c>.</summary>
    public static bool HasTokens(string? text) =>
        !string.IsNullOrEmpty(text) && text.IndexOf('{') >= 0 && SimpleToken().IsMatch(text);

    /// <summary>
    /// Distinct bare field identifiers referenced anywhere in the text. Built-in
    /// names (CurrentDate, PageNumber, ...) are included; callers filter as needed.
    /// </summary>
    public static IEnumerable<string> ExtractFieldNames(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in SimpleToken().Matches(text))
        {
            string inner = m.Groups[1].Value.Trim();

            // Bare "{Field Name}" — a single identifier, no function call / operator.
            if (Identifier().Match(inner) is { Success: true, Value: var whole } && whole == inner)
            {
                if (seen.Add(inner))
                {
                    yield return inner;
                }

                continue;
            }

            // Expression — pull identifiers that are not immediately followed by '('.
            foreach (Match id in Identifier().Matches(inner))
            {
                int after = id.Index + id.Length;
                bool isCall = after < inner.Length && inner[after] == '(';
                string name = id.Value.Trim();
                if (!isCall && name.Length > 0 && seen.Add(name))
                {
                    yield return name;
                }
            }
        }
    }
}
