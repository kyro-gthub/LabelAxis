using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace LabelMaker.UiTests;

/// <summary>
/// Runs the click-through tests against an installed (or built) LabelAxis and writes a report.
///
///   LabelAxis.UiTests.exe --test-pc [--app "C:\…\LabelAxis.exe"] [--only Arabic] [--out results]
///
/// The tests take over the keyboard focus and open windows for several minutes, so they run
/// only with --test-pc, on a PC set aside for testing. See docs/UI-TESTS.md.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!args.Contains("--test-pc", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("""
                LabelAxis click-through tests

                These tests start LabelAxis many times and press its buttons and menus through
                Windows UI Automation. While they run they take the keyboard focus and open
                windows on the screen, so run them only on a separate test PC that nobody is
                using, never on a PC you are working on.

                On the test PC:   LabelAxis.UiTests.exe --test-pc
                Options:          --app <path to LabelAxis.exe>   (default: the installed LabelAxis)
                                  --only <part of a test's name>
                                  --out <folder for the report>    (default: .\ui-test-results)
                """);
            return 2;
        }

        string exe = Option(args, "--app") ?? FindInstalledApp()
                     ?? throw new FileNotFoundException("LabelAxis is not installed here. Install it, or give its path with --app.");
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"Not found: {exe}");
            return 2;
        }

        string only = Option(args, "--only") ?? string.Empty;
        string output = Path.GetFullPath(Option(args, "--out") ?? "ui-test-results");
        Directory.CreateDirectory(output);

        var scenarios = typeof(Scenarios).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => (Method: m, Info: m.GetCustomAttribute<ScenarioAttribute>()))
            .Where(s => s.Info is not null && (only.Length == 0 || s.Method.Name.Contains(only, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Console.WriteLine($"LabelAxis click-through tests — {scenarios.Count} test(s) against {exe}");
        Console.WriteLine();

        var results = new List<(string Name, string Description, bool Passed, TimeSpan Time, string? Failure)>();
        foreach ((MethodInfo method, ScenarioAttribute? info) in scenarios)
        {
            var clock = Stopwatch.StartNew();
            string? failure = null;
            try
            {
                method.Invoke(null, new object[] { exe });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                failure = ex.InnerException is TestFailure ? ex.InnerException.Message : ex.InnerException.ToString();
                Screenshot(Path.Combine(output, method.Name + ".png"));
            }

            clock.Stop();
            results.Add((method.Name, info!.Description, failure is null, clock.Elapsed, failure));
            Console.ForegroundColor = failure is null ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(failure is null ? "  PASS  " : "  FAIL  ");
            Console.ResetColor();
            Console.WriteLine($"{info.Description} ({clock.Elapsed.TotalSeconds:0.0} s)");
            if (failure is not null)
            {
                Console.WriteLine("        " + failure.Split('\n')[0]);
            }
        }

        int failed = results.Count(r => !r.Passed);
        Console.WriteLine();
        Console.WriteLine($"{results.Count - failed} passed, {failed} failed. Report: {output}");
        WriteReports(output, exe, results);
        return failed == 0 ? 0 : 1;
    }

    private static string? Option(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // Where the installer puts LabelAxis: for the current user, or for all users.
    private static string? FindInstalledApp() =>
        new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LabelAxis", "LabelAxis.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LabelAxis", "LabelAxis.exe"),
        }.FirstOrDefault(File.Exists);

    private static void Screenshot(string path)
    {
        try
        {
            Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            using var bitmap = new Bitmap(screen.Width, screen.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
            }

            bitmap.Save(path, ImageFormat.Png);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ArgumentException or InvalidOperationException)
        {
            // No picture; the report still has the message.
        }
    }

    private static void WriteReports(string folder, string exe,
        List<(string Name, string Description, bool Passed, TimeSpan Time, string? Failure)> results)
    {
        var text = new StringBuilder();
        text.AppendLine($"LabelAxis click-through tests, {DateTime.Now:yyyy-MM-dd HH:mm}");
        text.AppendLine($"App: {exe} ({FileVersionInfo.GetVersionInfo(exe).ProductVersion})");
        text.AppendLine($"PC: {Environment.MachineName}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        text.AppendLine();
        foreach (var r in results)
        {
            text.AppendLine($"{(r.Passed ? "PASS" : "FAIL")}  {r.Description}  ({r.Time.TotalSeconds:0.0} s)");
            if (r.Failure is not null)
            {
                text.AppendLine("      " + r.Failure.Replace("\n", "\n      ", StringComparison.Ordinal));
                text.AppendLine($"      Screen at the failure: {r.Name}.png");
            }
        }

        File.WriteAllText(Path.Combine(folder, "report.txt"), text.ToString());

        // The JUnit format, which build servers understand.
        var suite = new XElement("testsuite",
            new XAttribute("name", "LabelAxis.UiTests"),
            new XAttribute("tests", results.Count),
            new XAttribute("failures", results.Count(r => !r.Passed)),
            new XAttribute("time", results.Sum(r => r.Time.TotalSeconds).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)),
            results.Select(r => new XElement("testcase",
                new XAttribute("classname", "LabelAxis.UiTests.Scenarios"),
                new XAttribute("name", r.Description),
                new XAttribute("time", r.Time.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)),
                r.Failure is null ? null : new XElement("failure", new XAttribute("message", r.Failure.Split('\n')[0]), r.Failure))));
        new XDocument(suite).Save(Path.Combine(folder, "results.xml"));
    }
}
