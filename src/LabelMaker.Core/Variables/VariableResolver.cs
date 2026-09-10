using System.Globalization;
using System.Text;
using LabelMaker.Core.Model;

namespace LabelMaker.Core.Variables;

/// <summary>Per-label inputs the resolver needs beyond the data record (spec §10).</summary>
public sealed class ResolveContext
{
    /// <summary>Current data record as field name → raw string. Null for static content.</summary>
    public IReadOnlyDictionary<string, string>? Record { get; init; }

    public IReadOnlyList<CustomVariable> Variables { get; init; } = Array.Empty<CustomVariable>();

    /// <summary>Resolved sequence values keyed by sequence id, already advanced for this label.</summary>
    public IReadOnlyDictionary<string, string> SequenceValues { get; init; } =
        new Dictionary<string, string>();

    public int RecordNumber { get; init; } = 1;
    public int RecordCount { get; init; } = 1;
    public int PageNumber { get; init; } = 1;
    public int LabelPosition { get; init; } = 1;
    public string TemplateName { get; init; } = string.Empty;

    public DateTime Now { get; init; } = DateTime.Now;

    /// <summary>Culture used for built-in date formatting (files stay invariant).</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;
}

/// <summary>
/// Replaces <c>{token}</c> placeholders in authored strings with resolved values.
/// A token is either a bare field/variable/built-in name or a sandboxed
/// expression. Authored text is never mutated — resolution is a pure transform
/// (spec §30).
/// </summary>
public sealed class VariableResolver : IFieldValueSource
{
    private readonly ResolveContext _ctx;
    private readonly Dictionary<string, CustomVariable> _variables;
    private readonly HashSet<string> _resolving = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExpressionEngine _engine;

    public VariableResolver(ResolveContext context)
    {
        _ctx = context ?? throw new ArgumentNullException(nameof(context));
        _variables = context.Variables.ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);
        _engine = new ExpressionEngine(this, context.Now);
    }

    /// <summary>Resolve every <c>{...}</c> in <paramref name="authored"/>. Unknown tokens become "".</summary>
    public string Resolve(string? authored)
    {
        if (string.IsNullOrEmpty(authored) || authored.IndexOf('{') < 0)
        {
            return authored ?? string.Empty;
        }

        var sb = new StringBuilder(authored.Length + 16);
        int i = 0;
        while (i < authored.Length)
        {
            char c = authored[i];
            if (c != '{')
            {
                sb.Append(c);
                i++;
                continue;
            }

            int end = FindMatchingBrace(authored, i);
            if (end < 0)
            {
                sb.Append(authored[i..]); // unbalanced — emit literally
                break;
            }

            string inner = authored.Substring(i + 1, end - i - 1).Trim();
            sb.Append(ResolveToken(inner));
            i = end + 1;
        }

        return sb.ToString();
    }

    /// <summary>Resolve a single token body (no surrounding braces).</summary>
    public string ResolveToken(string inner)
    {
        if (inner.Length == 0)
        {
            return string.Empty;
        }

        if (IsBareName(inner))
        {
            return LookupName(inner)?.AsText() ?? string.Empty;
        }

        try
        {
            return _engine.EvaluateToString(inner);
        }
        catch (ExpressionException)
        {
            return string.Empty;
        }
    }

    bool IFieldValueSource.TryGetField(string name, out ExprValue value)
    {
        ExprValue? v = LookupName(name);
        value = v ?? ExprValue.Empty;
        return true;
    }

    private ExprValue? LookupName(string name)
    {
        // 1. Built-ins
        switch (name.ToUpperInvariant())
        {
            case "CURRENTDATE":
            case "TODAY":
                return ExprValue.Text(_ctx.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            case "CURRENTTIME":
                return ExprValue.Text(_ctx.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
            case "CURRENTDATETIME":
            case "NOW":
                return ExprValue.Text(_ctx.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            case "PAGENUMBER":
                return ExprValue.Number(_ctx.PageNumber);
            case "RECORDNUMBER":
            case "RECORDINDEX":
                return ExprValue.Number(_ctx.RecordNumber);
            case "RECORDCOUNT":
                return ExprValue.Number(_ctx.RecordCount);
            case "LABELPOSITION":
                return ExprValue.Number(_ctx.LabelPosition);
            case "TEMPLATENAME":
                return ExprValue.Text(_ctx.TemplateName);
        }

        // 2. Data record columns
        if (_ctx.Record is not null && _ctx.Record.TryGetValue(name, out string? cell))
        {
            return ExprValue.Text(cell ?? string.Empty);
        }

        // 3. Custom variables (guarded against cycles)
        if (_variables.TryGetValue(name, out CustomVariable? variable))
        {
            return ResolveVariable(variable);
        }

        // 4. Sequence by name → id
        foreach (KeyValuePair<string, string> kv in _ctx.SequenceValues)
        {
            if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return ExprValue.Text(kv.Value);
            }
        }

        return null;
    }

    private ExprValue ResolveVariable(CustomVariable variable)
    {
        if (!_resolving.Add(variable.Name))
        {
            return ExprValue.Text(string.Empty); // cycle guard
        }

        try
        {
            switch (variable.Kind)
            {
                case VariableKind.Constant:
                    return ExprValue.Text(variable.Value);
                case VariableKind.Sequence:
                    return ExprValue.Text(_ctx.SequenceValues.GetValueOrDefault(variable.Value, string.Empty));
                case VariableKind.Expression:
                    try
                    {
                        return _engine.Evaluate(variable.Value);
                    }
                    catch (ExpressionException)
                    {
                        return ExprValue.Text(string.Empty);
                    }

                default:
                    return ExprValue.Empty;
            }
        }
        finally
        {
            _resolving.Remove(variable.Name);
        }
    }

    private static bool IsBareName(string s)
    {
        foreach (char c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != ' ')
            {
                return false;
            }
        }

        return s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_');
    }

    private static int FindMatchingBrace(string s, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < s.Length; i++)
        {
            if (s[i] == '{')
            {
                depth++;
            }
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}
