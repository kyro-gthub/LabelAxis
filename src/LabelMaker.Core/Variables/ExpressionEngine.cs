using System.Globalization;
using System.Text;

namespace LabelMaker.Core.Variables;

/// <summary>Supplies field values to the expression engine. Implementations never expose host objects.</summary>
public interface IFieldValueSource
{
    bool TryGetField(string name, out ExprValue value);
}

/// <summary>
/// A deliberately tiny, sandboxed expression evaluator for variable-data
/// templates (spec §10, §28). It supports:
/// <list type="bullet">
///   <item>string / number / date / boolean literals</item>
///   <item><c>{Field}</c> references resolved through an <see cref="IFieldValueSource"/></item>
///   <item><c>+ - * / %</c>, unary <c>-</c>, comparisons, <c>&amp;</c> for string join</item>
///   <item>a fixed whitelist of pure functions</item>
/// </list>
/// There are no variables to assign, no loops, no I/O, no reflection, and hard
/// limits on input length, AST size, recursion depth and produced string length.
/// Opening or rendering a project can never execute arbitrary code.
/// </summary>
public sealed class ExpressionEngine
{
    public const int MaxExpressionLength = 4000;
    public const int MaxNodes = 800;
    public const int MaxDepth = 48;
    public const int MaxStringLength = 100_000;

    private readonly IFieldValueSource _fields;
    private readonly DateTime _now;
    private int _nodeBudget;

    public ExpressionEngine(IFieldValueSource fields, DateTime? now = null)
    {
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _now = now ?? DateTime.Now;
    }

    public string EvaluateToString(string expression) => Evaluate(expression).AsText();

    public ExprValue Evaluate(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Length > MaxExpressionLength)
        {
            throw new ExpressionException($"Expression exceeds {MaxExpressionLength} characters.");
        }

        _nodeBudget = MaxNodes;
        var parser = new Parser(expression, this);
        ExprValue result = parser.ParseProgram();
        return result;
    }

    /// <summary>True if the expression parses and references only known functions (does not evaluate).</summary>
    public static bool Validate(string expression, out string? error)
    {
        try
        {
            var engine = new ExpressionEngine(EmptyFields.Instance);
            engine._nodeBudget = MaxNodes;
            var parser = new Parser(expression, engine, checkOnly: true);
            parser.ParseProgram();
            error = null;
            return true;
        }
        catch (ExpressionException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void ConsumeNode()
    {
        if (--_nodeBudget < 0)
        {
            throw new ExpressionException("Expression is too complex.");
        }
    }

    private ExprValue CallFunction(string name, List<ExprValue> args)
    {
        switch (name.ToUpperInvariant())
        {
            case "UPPER": return Str(Arg(args, 0).AsText().ToUpperInvariant());
            case "LOWER": return Str(Arg(args, 0).AsText().ToLowerInvariant());
            case "TRIM": return Str(Arg(args, 0).AsText().Trim());
            case "LEN": return ExprValue.Number(Arg(args, 0).AsText().Length);
            case "LEFT": return Str(Slice(Arg(args, 0).AsText(), 0, (int)Arg(args, 1).AsNumber()));
            case "RIGHT":
            {
                string s = Arg(args, 0).AsText();
                int n = Math.Clamp((int)Arg(args, 1).AsNumber(), 0, s.Length);
                return Str(s[(s.Length - n)..]);
            }

            case "MID":
            {
                string s = Arg(args, 0).AsText();
                int start = Math.Clamp((int)Arg(args, 1).AsNumber(), 0, s.Length);
                int len = args.Count > 2 ? (int)Arg(args, 2).AsNumber() : s.Length - start;
                return Str(Slice(s, start, len));
            }

            case "CONCAT":
            {
                var sb = new StringBuilder();
                foreach (ExprValue a in args)
                {
                    Append(sb, a.AsText());
                }

                return Str(sb.ToString());
            }

            case "REPLACE":
            {
                string s = Arg(args, 0).AsText();
                string find = Arg(args, 1).AsText();
                string rep = Arg(args, 2).AsText();
                string result = find.Length == 0 ? s : s.Replace(find, rep, StringComparison.Ordinal);
                Guard(result);
                return Str(result);
            }

            case "PAD":
            case "PADLEFT":
            {
                string s = Arg(args, 0).AsText();
                int width = (int)Arg(args, 1).AsNumber();
                char pad = args.Count > 2 && Arg(args, 2).AsText() is { Length: > 0 } p ? p[0] : '0';
                return Str(width > MaxStringLength ? throw Big() : s.PadLeft(Math.Max(0, width), pad));
            }

            case "FORMAT":
            {
                double n = Arg(args, 0).AsNumber();
                string fmt = Arg(args, 1).AsText();
                return Str(n.ToString(fmt, CultureInfo.InvariantCulture));
            }

            case "NUMBER": return ExprValue.Number(Arg(args, 0).AsNumber());
            case "ROUND":
            {
                int digits = args.Count > 1 ? (int)Arg(args, 1).AsNumber() : 0;
                return ExprValue.Number(Math.Round(Arg(args, 0).AsNumber(), Math.Clamp(digits, 0, 15)));
            }

            case "ABS": return ExprValue.Number(Math.Abs(Arg(args, 0).AsNumber()));
            case "MIN": return ExprValue.Number(args.Min(a => a.AsNumber()));
            case "MAX": return ExprValue.Number(args.Max(a => a.AsNumber()));

            case "TODAY": return ExprValue.Date(_now.Date);
            case "NOW": return ExprValue.Date(_now);
            case "YEAR": return ExprValue.Number(Arg(args, 0).AsDate().Year);
            case "MONTH": return ExprValue.Number(Arg(args, 0).AsDate().Month);
            case "DAY": return ExprValue.Number(Arg(args, 0).AsDate().Day);
            case "ADDDAYS": return ExprValue.Date(Arg(args, 0).AsDate().AddDays(Arg(args, 1).AsNumber()));
            case "ADDMONTHS": return ExprValue.Date(Arg(args, 0).AsDate().AddMonths((int)Arg(args, 1).AsNumber()));
            case "DATEFORMAT":
            case "TEXTDATE": return Str(Arg(args, 0).AsDate().ToString(Arg(args, 1).AsText(), CultureInfo.InvariantCulture));

            case "IF": return Arg(args, 0).AsBoolean() ? Arg(args, 1) : Arg(args, 2);
            case "IFEMPTY":
            {
                string s = Arg(args, 0).AsText();
                return s.Length == 0 ? Arg(args, 1) : ExprValue.Text(s);
            }

            case "CONTAINS": return ExprValue.Boolean(Arg(args, 0).AsText().Contains(Arg(args, 1).AsText(), StringComparison.OrdinalIgnoreCase));
            case "STARTSWITH": return ExprValue.Boolean(Arg(args, 0).AsText().StartsWith(Arg(args, 1).AsText(), StringComparison.OrdinalIgnoreCase));

            default:
                throw new ExpressionException($"Unknown function '{name}'.");
        }

        static ExprValue Arg(List<ExprValue> a, int i) =>
            i < a.Count ? a[i] : throw new ExpressionException("Missing function argument.");

        static string Slice(string s, int start, int len)
        {
            if (start < 0)
            {
                start = 0;
            }

            if (start >= s.Length || len <= 0)
            {
                return string.Empty;
            }

            len = Math.Min(len, s.Length - start);
            return s.Substring(start, len);
        }
    }

    private static ExprValue Str(string s)
    {
        Guard(s);
        return ExprValue.Text(s);
    }

    private static void Append(StringBuilder sb, string s)
    {
        if (sb.Length + s.Length > MaxStringLength)
        {
            throw Big();
        }

        sb.Append(s);
    }

    private static void Guard(string s)
    {
        if (s.Length > MaxStringLength)
        {
            throw Big();
        }
    }

    private static ExpressionException Big() => new($"Result string exceeds {MaxStringLength} characters.");

    private sealed class EmptyFields : IFieldValueSource
    {
        public static readonly EmptyFields Instance = new();

        public bool TryGetField(string name, out ExprValue value)
        {
            value = ExprValue.Empty;
            return true;
        }
    }

    // ---- recursive-descent parser + evaluator ----

    private sealed class Parser
    {
        private readonly string _src;
        private readonly ExpressionEngine _engine;
        private readonly bool _checkOnly;
        private int _pos;
        private int _depth;

        public Parser(string src, ExpressionEngine engine, bool checkOnly = false)
        {
            _src = src;
            _engine = engine;
            _checkOnly = checkOnly;
        }

        public ExprValue ParseProgram()
        {
            ExprValue v = ParseExpression();
            SkipWs();
            if (_pos != _src.Length)
            {
                throw new ExpressionException($"Unexpected '{_src[_pos]}' at position {_pos}.");
            }

            return v;
        }

        private ExprValue ParseExpression() => ParseComparison();

        private ExprValue ParseComparison()
        {
            ExprValue left = ParseAdditive();
            SkipWs();
            while (TryMatchOperator(out string op, "==", "!=", "<=", ">=", "<", ">"))
            {
                ExprValue right = ParseAdditive();
                left = Compare(left, right, op);
                SkipWs();
            }

            return left;
        }

        private ExprValue ParseAdditive()
        {
            ExprValue left = ParseMultiplicative();
            SkipWs();
            while (_pos < _src.Length && _src[_pos] is '+' or '-' or '&')
            {
                char op = _src[_pos++];
                ExprValue right = ParseMultiplicative();
                _engine.ConsumeNode();
                left = op switch
                {
                    '&' => Str(left.AsText() + right.AsText()),
                    '+' when left.Kind == ExprValueKind.Text || right.Kind == ExprValueKind.Text
                        => Str(left.AsText() + right.AsText()),
                    '+' => ExprValue.Number(left.AsNumber() + right.AsNumber()),
                    _ => ExprValue.Number(left.AsNumber() - right.AsNumber()),
                };
                SkipWs();
            }

            return left;
        }

        private ExprValue ParseMultiplicative()
        {
            ExprValue left = ParseUnary();
            SkipWs();
            while (_pos < _src.Length && _src[_pos] is '*' or '/' or '%')
            {
                char op = _src[_pos++];
                ExprValue right = ParseUnary();
                _engine.ConsumeNode();
                double a = left.AsNumber();
                double b = right.AsNumber();
                left = op switch
                {
                    '*' => ExprValue.Number(a * b),
                    '/' => ExprValue.Number(b == 0 ? throw new ExpressionException("Division by zero.") : a / b),
                    _ => ExprValue.Number(b == 0 ? throw new ExpressionException("Modulo by zero.") : a % b),
                };
                SkipWs();
            }

            return left;
        }

        private ExprValue ParseUnary()
        {
            SkipWs();
            if (_pos < _src.Length && _src[_pos] == '-')
            {
                _pos++;
                return ExprValue.Number(-ParseUnary().AsNumber());
            }

            if (_pos < _src.Length && _src[_pos] == '+')
            {
                _pos++;
                return ParseUnary();
            }

            return ParsePrimary();
        }

        private ExprValue ParsePrimary()
        {
            SkipWs();
            _engine.ConsumeNode();
            if (++_depth > MaxDepth)
            {
                throw new ExpressionException("Expression nesting is too deep.");
            }

            try
            {
                if (_pos >= _src.Length)
                {
                    throw new ExpressionException("Unexpected end of expression.");
                }

                char c = _src[_pos];

                if (c == '(')
                {
                    _pos++;
                    ExprValue v = ParseExpression();
                    Expect(')');
                    return v;
                }

                if (c is '"' or '\'')
                {
                    return ExprValue.Text(ParseStringLiteral(c));
                }

                if (c == '{')
                {
                    return ParseFieldRef();
                }

                if (char.IsDigit(c) || (c == '.' && _pos + 1 < _src.Length && char.IsDigit(_src[_pos + 1])))
                {
                    return ParseNumber();
                }

                if (char.IsLetter(c) || c == '_')
                {
                    return ParseIdentifierOrCall();
                }

                throw new ExpressionException($"Unexpected character '{c}' at position {_pos}.");
            }
            finally
            {
                _depth--;
            }
        }

        private ExprValue ParseFieldRef()
        {
            Expect('{');
            int start = _pos;
            while (_pos < _src.Length && _src[_pos] != '}')
            {
                _pos++;
            }

            if (_pos >= _src.Length)
            {
                throw new ExpressionException("Unclosed '{' in field reference.");
            }

            string name = _src[start.._pos].Trim();
            _pos++; // consume '}'

            if (name.Length == 0)
            {
                throw new ExpressionException("Empty field reference '{}'.");
            }

            if (_checkOnly)
            {
                return ExprValue.Empty;
            }

            return _engine._fields.TryGetField(name, out ExprValue value) ? value : ExprValue.Empty;
        }

        private ExprValue ParseIdentifierOrCall()
        {
            int start = _pos;
            while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            {
                _pos++;
            }

            string ident = _src[start.._pos];
            SkipWs();

            if (_pos < _src.Length && _src[_pos] == '(')
            {
                _pos++;
                var args = new List<ExprValue>();
                SkipWs();
                if (_pos < _src.Length && _src[_pos] == ')')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        args.Add(ParseExpression());
                        SkipWs();
                        if (_pos < _src.Length && _src[_pos] == ',')
                        {
                            _pos++;
                            continue;
                        }

                        Expect(')');
                        break;
                    }
                }

                _engine.ConsumeNode();
                return _checkOnly ? ProbeFunction(ident, args.Count) : _engine.CallFunction(ident, args);
            }

            return ident.ToUpperInvariant() switch
            {
                "TRUE" => ExprValue.Boolean(true),
                "FALSE" => ExprValue.Boolean(false),
                "NULL" or "EMPTY" => ExprValue.Empty,
                _ => throw new ExpressionException(
                    $"'{ident}' is not a known value. Wrap field names in braces: {{{ident}}}."),
            };
        }

        private static ExprValue ProbeFunction(string name, int argCount)
        {
            string[] known =
            {
                "UPPER", "LOWER", "TRIM", "LEN", "LEFT", "RIGHT", "MID", "CONCAT", "REPLACE", "PAD", "PADLEFT",
                "FORMAT", "NUMBER", "ROUND", "ABS", "MIN", "MAX", "TODAY", "NOW", "YEAR", "MONTH", "DAY",
                "ADDDAYS", "ADDMONTHS", "DATEFORMAT", "TEXTDATE", "IF", "IFEMPTY", "CONTAINS", "STARTSWITH",
            };
            if (!known.Contains(name.ToUpperInvariant()))
            {
                throw new ExpressionException($"Unknown function '{name}'.");
            }

            _ = argCount;
            return ExprValue.Empty;
        }

        private ExprValue ParseNumber()
        {
            int start = _pos;
            while (_pos < _src.Length && (char.IsDigit(_src[_pos]) || _src[_pos] == '.'))
            {
                _pos++;
            }

            string text = _src[start.._pos];
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? ExprValue.Number(d)
                : throw new ExpressionException($"'{text}' is not a valid number.");
        }

        private string ParseStringLiteral(char quote)
        {
            _pos++; // opening quote
            var sb = new StringBuilder();
            while (_pos < _src.Length)
            {
                char c = _src[_pos++];
                if (c == '\\' && _pos < _src.Length)
                {
                    char next = _src[_pos++];
                    sb.Append(next switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        _ => next,
                    });
                    continue;
                }

                if (c == quote)
                {
                    string s = sb.ToString();
                    Guard(s);
                    return s;
                }

                sb.Append(c);
            }

            throw new ExpressionException("Unterminated string literal.");
        }

        private static ExprValue Compare(ExprValue a, ExprValue b, string op)
        {
            int cmp;
            if (a.IsNumber || b.IsNumber)
            {
                cmp = a.AsNumber().CompareTo(b.AsNumber());
            }
            else
            {
                cmp = string.CompareOrdinal(a.AsText(), b.AsText());
            }

            return ExprValue.Boolean(op switch
            {
                "==" => cmp == 0,
                "!=" => cmp != 0,
                "<" => cmp < 0,
                ">" => cmp > 0,
                "<=" => cmp <= 0,
                ">=" => cmp >= 0,
                _ => false,
            });
        }

        private bool TryMatchOperator(out string op, params string[] candidates)
        {
            SkipWs();
            foreach (string candidate in candidates)
            {
                if (_pos + candidate.Length <= _src.Length &&
                    _src.AsSpan(_pos, candidate.Length).SequenceEqual(candidate))
                {
                    _pos += candidate.Length;
                    op = candidate;
                    return true;
                }
            }

            op = string.Empty;
            return false;
        }

        private void Expect(char c)
        {
            SkipWs();
            if (_pos >= _src.Length || _src[_pos] != c)
            {
                throw new ExpressionException($"Expected '{c}' at position {_pos}.");
            }

            _pos++;
        }

        private void SkipWs()
        {
            while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos]))
            {
                _pos++;
            }
        }
    }
}
