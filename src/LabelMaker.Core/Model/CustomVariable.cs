namespace LabelMaker.Core.Model;

public enum VariableKind
{
    /// <summary>A fixed literal value.</summary>
    Constant = 0,

    /// <summary>An expression evaluated against the current record + other variables.</summary>
    Expression = 1,

    /// <summary>Bound to a named sequence; advances per label.</summary>
    Sequence = 2,
}

/// <summary>
/// A user-defined <c>{Name}</c> usable anywhere a token is accepted (spec §10).
/// Resolution order and sandboxing are handled by <c>VariableResolver</c>.
/// </summary>
public sealed class CustomVariable
{
    public string Name { get; set; } = "Var";

    public VariableKind Kind { get; set; } = VariableKind.Constant;

    /// <summary>Literal value, expression text, or sequence id depending on <see cref="Kind"/>.</summary>
    public string Value { get; set; } = string.Empty;

    public string? Description { get; set; }

    public CustomVariable Clone() => (CustomVariable)MemberwiseClone();
}
