using System.Runtime.ExceptionServices;

namespace LabelMaker.App.Tests;

/// <summary>
/// Runs a test body on a thread of its own set up the way WPF needs (single-threaded
/// apartment), as the app's own window thread is. Failures come back as they were thrown.
/// </summary>
public static class Sta
{
    static Sta()
    {
        // Starting WPF registers "pack://" addresses, which the app's own fonts and pictures use.
        // Nothing starts WPF here, so the address type is registered by hand.
        if (!UriParser.IsKnownScheme("pack"))
        {
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        }
    }

    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
