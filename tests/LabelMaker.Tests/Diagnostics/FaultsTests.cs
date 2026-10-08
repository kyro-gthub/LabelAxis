using System.Reflection;
using FluentAssertions;
using LabelMaker.Core.Diagnostics;
using Xunit;

namespace LabelMaker.Tests.Diagnostics;

/// <summary>Which failures are told to the user and which are mistakes that belong in the crash log.</summary>
public sealed class FaultsTests
{
    public static TheoryData<Exception> OutsideFailures => new()
    {
        new IOException("The file is in use"),
        new UnauthorizedAccessException(),
        new FormatException("Not a number"),
        new InvalidOperationException("The printer is offline"),
        new ArgumentException("Bad value", "value"),
        new System.ComponentModel.Win32Exception(5),
        new OperationCanceledException(),
    };

    public static TheoryData<Exception> Mistakes => new()
    {
        Caught(() => _ = Nothing()!.Length),
        Caught(() => _ = Array.Empty<int>()[1]),
        new InvalidCastException(),
        new ArgumentNullException("value"),
        new KeyNotFoundException(),
        new NotImplementedException(),
        new AggregateException(new IOException(), Caught(() => _ = Nothing()!.Length)),
        new TargetInvocationException(new InvalidCastException()),
    };

    // The runtime's own exceptions are made by doing what throws them, not by hand.
    private static Exception Caught(Action mistake)
    {
        try
        {
            mistake();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("It did not fail.");
    }

    private static string? Nothing() => null;

    [Theory]
    [MemberData(nameof(OutsideFailures))]
    public void A_failure_outside_the_program_is_told_to_the_user(Exception ex)
    {
        Faults.IsReportable(ex).Should().BeTrue();
        Faults.IsProgramFault(ex).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public void A_mistake_in_the_program_is_not_dressed_up_as_a_message(Exception ex) =>
        Faults.IsReportable(ex).Should().BeFalse();

    [Fact]
    public void A_mistake_survived_while_drawing_is_logged_once_for_each_place_it_happens()
    {
        var logged = new List<Exception>();
        Action<Exception>? before = Faults.Log;
        Faults.Log = ex => { lock (logged) { logged.Add(ex); } };
        try
        {
            Exception first = Thrown(), second = Thrown();
            Faults.Survived(first);
            Faults.Survived(second);
            Faults.Survived(new IOException("an ordinary failure is not a mistake"));

            lock (logged)
            {
                logged.Where(e => e == first || e == second).Should().ContainSingle();
                logged.Should().NotContain(e => e is IOException);
            }
        }
        finally
        {
            Faults.Log = before;
        }

        static Exception Thrown() => Caught(() => _ = Array.Empty<int>()[1]);
    }

    [Fact]
    public void Without_a_log_nothing_is_written_and_nothing_fails() =>
        FluentActions.Invoking(() => Faults.Survived(Caught(() => _ = Nothing()!.Length))).Should().NotThrow();
}
