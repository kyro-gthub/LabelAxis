using System.Globalization;

namespace LabelMaker.Core.Variables;

public enum ExprValueKind
{
    Text = 0,
    Number = 1,
    Date = 2,
    Boolean = 3,
}

/// <summary>A value flowing through the expression engine. Immutable, no references to host state.</summary>
public readonly struct ExprValue
{
    private readonly object _value;

    private ExprValue(ExprValueKind kind, object value)
    {
        Kind = kind;
        _value = value;
    }

    public ExprValueKind Kind { get; }

    public static readonly ExprValue Empty = Text(string.Empty);

    public static ExprValue Text(string s) => new(ExprValueKind.Text, s ?? string.Empty);
    public static ExprValue Number(double d) => new(ExprValueKind.Number, d);
    public static ExprValue Date(DateTime dt) => new(ExprValueKind.Date, dt);
    public static ExprValue Boolean(bool b) => new(ExprValueKind.Boolean, b);

    public bool IsNumber => Kind == ExprValueKind.Number;

    public double AsNumber() => Kind switch
    {
        ExprValueKind.Number => (double)_value,
        ExprValueKind.Boolean => (bool)_value ? 1 : 0,
        ExprValueKind.Text => double.TryParse((string)_value, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)
            ? d
            : throw new ExpressionException($"'{_value}' is not a number."),
        ExprValueKind.Date => ((DateTime)_value).ToOADate(),
        _ => 0,
    };

    public bool AsBoolean() => Kind switch
    {
        ExprValueKind.Boolean => (bool)_value,
        ExprValueKind.Number => Math.Abs((double)_value) > double.Epsilon,
        ExprValueKind.Text => ((string)_value).Length > 0,
        ExprValueKind.Date => true,
        _ => false,
    };

    public DateTime AsDate() => Kind switch
    {
        ExprValueKind.Date => (DateTime)_value,
        ExprValueKind.Text => DateTime.TryParse((string)_value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt)
            ? dt
            : throw new ExpressionException($"'{_value}' is not a date."),
        _ => throw new ExpressionException("Value is not a date."),
    };

    public string AsText() => Kind switch
    {
        ExprValueKind.Text => (string)_value,
        ExprValueKind.Number => FormatNumber((double)_value),
        ExprValueKind.Boolean => (bool)_value ? "true" : "false",
        ExprValueKind.Date => ((DateTime)_value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    private static string FormatNumber(double d) =>
        d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e15
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("0.##########", CultureInfo.InvariantCulture);

    public override string ToString() => AsText();
}

public sealed class ExpressionException : Exception
{
    public ExpressionException(string message) : base(message)
    {
    }
}
