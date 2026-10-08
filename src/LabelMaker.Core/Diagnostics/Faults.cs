using System.Reflection;

namespace LabelMaker.Core.Diagnostics;

/// <summary>
/// Tells a failure in the world outside LabelAxis (a missing, locked or damaged file, a printer
/// or driver that refuses, a value a barcode cannot hold) from a mistake in LabelAxis's own code.
/// <para>
/// A catch that tells the user what went wrong filters with <see cref="IsReportable"/>, so a
/// mistake is not dressed up as an ordinary message: it reaches the app's crash log, where it
/// is seen and fixed. Code that must carry on whatever happens (drawing a page, falling back to
/// another font) calls <see cref="Survived"/>, which writes a mistake to the log the same way.
/// </para>
/// </summary>
public static class Faults
{
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    /// <summary>
    /// Where <see cref="Survived"/> writes a mistake: the app's crash log. Set once at start-up;
    /// nothing is written while it is null (the tests, the command-line tool).
    /// </summary>
    public static Action<Exception>? Log { get; set; }

    /// <summary>True for the exceptions only a mistake in the program throws.</summary>
    public static bool IsProgramFault(Exception ex) => ex switch
    {
        NullReferenceException or IndexOutOfRangeException or InvalidCastException or ArgumentNullException
            or KeyNotFoundException or NotImplementedException or DivideByZeroException
            or MissingMemberException or TypeLoadException or InvalidProgramException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsProgramFault),
        TargetInvocationException { InnerException: { } inner } => IsProgramFault(inner),
        _ => false,
    };

    /// <summary>True when <paramref name="ex"/> is a failure to tell the user about rather than a mistake in LabelAxis.</summary>
    public static bool IsReportable(Exception ex) => !IsProgramFault(ex);

    /// <summary>
    /// For code that carries on after any failure: writes <paramref name="ex"/> to the log when
    /// it is a mistake in LabelAxis, once for each place it happens (a page drawn again and again
    /// would otherwise write it every time).
    /// </summary>
    public static void Survived(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (!IsProgramFault(ex) || Log is not { } log)
        {
            return;
        }

        string where = ex.GetType().FullName + "\n" + ex.StackTrace;
        lock (Logged)
        {
            if (!Logged.Add(where))
            {
                return;
            }
        }

        log(ex);
    }
}
