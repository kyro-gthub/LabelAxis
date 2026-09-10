using FluentAssertions;
using LabelMaker.Core.Variables;
using Xunit;

namespace LabelMaker.Tests.Variables;

public sealed class ExpressionEngineTests
{
    private sealed class Fields : IFieldValueSource
    {
        private readonly Dictionary<string, string> _map;

        public Fields(Dictionary<string, string> map) => _map = map;

        public bool TryGetField(string name, out ExprValue value)
        {
            value = _map.TryGetValue(name, out string? v) ? ExprValue.Text(v) : ExprValue.Empty;
            return true;
        }
    }

    private static ExpressionEngine Engine(params (string, string)[] fields) =>
        new(new Fields(fields.ToDictionary(f => f.Item1, f => f.Item2)), new DateTime(2026, 1, 15, 9, 30, 0));

    [Theory]
    [InlineData("\"LOT-\" + {Batch}", "LOT-42")]
    [InlineData("UPPER({Product})", "WIDGET")]
    [InlineData("LEFT({Barcode}, 5)", "ABC00")]
    [InlineData("FORMAT({Price}, \"0.00\")", "15.00")]
    [InlineData("{Product} & \" x\" & {Qty}", "Widget x3")]
    [InlineData("IF({Qty} > 2, \"bulk\", \"single\")", "bulk")]
    [InlineData("CONCAT({Product}, \"-\", {Batch})", "Widget-42")]
    public void Evaluates_documented_examples(string expression, string expected)
    {
        ExpressionEngine engine = Engine(
            ("Batch", "42"),
            ("Product", "Widget"),
            ("Barcode", "ABC001459"),
            ("Price", "15"),
            ("Qty", "3"));

        engine.EvaluateToString(expression).Should().Be(expected);
    }

    [Fact]
    public void Date_functions_use_the_injected_clock()
    {
        ExpressionEngine engine = Engine();
        engine.EvaluateToString("DATEFORMAT(TODAY(), \"yyyy-MM-dd\")").Should().Be("2026-01-15");
        engine.EvaluateToString("DATEFORMAT(ADDDAYS(TODAY(), 30), \"yyyy-MM-dd\")").Should().Be("2026-02-14");
    }

    [Fact]
    public void Division_by_zero_is_a_handled_error_not_a_crash()
    {
        Action act = () => Engine().Evaluate("1 / 0");
        act.Should().Throw<ExpressionException>();
    }

    [Fact]
    public void Unknown_function_is_rejected()
    {
        ExpressionEngine.Validate("SYSTEM(\"rm -rf\")", out string? error).Should().BeFalse();
        error.Should().Contain("Unknown function");
    }

    [Fact]
    public void Bare_identifiers_are_rejected_to_prevent_ambiguity()
    {
        ExpressionEngine.Validate("Product + 1", out _).Should().BeFalse();
        ExpressionEngine.Validate("{Product}", out _).Should().BeTrue();
    }

    [Fact]
    public void Deeply_nested_expression_is_bounded()
    {
        string bomb = string.Concat(Enumerable.Repeat("UPPER(", 100)) + "\"x\"" + new string(')', 100);
        Action act = () => Engine().Evaluate(bomb);
        act.Should().Throw<ExpressionException>();
    }

    [Fact]
    public void Oversized_input_is_rejected_before_parsing()
    {
        string big = "\"" + new string('a', ExpressionEngine.MaxExpressionLength + 1) + "\"";
        Action act = () => Engine().Evaluate(big);
        act.Should().Throw<ExpressionException>();
    }

    [Fact]
    public void Resolver_replaces_tokens_without_mutating_source_semantics()
    {
        var ctx = new ResolveContext
        {
            Record = new Dictionary<string, string> { ["Batch"] = "42", ["Product"] = "Widget" },
            Now = new DateTime(2026, 1, 15),
        };
        var resolver = new VariableResolver(ctx);

        resolver.Resolve("Lot {Batch} — {UPPER({Product})}").Should().Be("Lot 42 — WIDGET");
        resolver.Resolve("no tokens here").Should().Be("no tokens here");
        resolver.Resolve("{MissingField}").Should().BeEmpty();
    }
}
