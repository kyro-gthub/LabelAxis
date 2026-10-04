using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;

namespace LabelMaker.UiTests;

/// <summary>
/// One running LabelAxis with a data folder of its own (<c>--data-dir</c>), so every test starts
/// from known settings and the test PC's own settings are never touched. Elements are found by
/// their automation ids, which do not change with the interface language.
/// </summary>
internal sealed class AppDriver : IDisposable
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private AppDriver(Process process, string dataDirectory, AutomationElement main)
    {
        Process = process;
        DataDirectory = dataDirectory;
        Main = main;
    }

    public Process Process { get; }

    public string DataDirectory { get; }

    public AutomationElement Main { get; private set; }

    public string SettingsFile => Path.Combine(DataDirectory, "LabelAxis", "settings.json");

    /// <summary>
    /// Starts LabelAxis with fresh settings: no welcome window, no update check, and the given
    /// language. <paramref name="settings"/> can change more before it starts.
    /// </summary>
    public static AppDriver Start(string exe, string language = "en", Action<JsonObject>? settings = null)
    {
        string data = Path.Combine(Path.GetTempPath(), "LabelAxis-UiTests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(data, "LabelAxis"));
        var json = new JsonObject
        {
            ["Language"] = language,
            ["ShowWelcomeOnStart"] = false,
            ["ReopenLastProjectOnStart"] = false,
            ["CheckForUpdates"] = false,
            ["AutoSaveEnabled"] = false,
        };
        settings?.Invoke(json);
        File.WriteAllText(Path.Combine(data, "LabelAxis", "settings.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(data);
        Process process = Process.Start(start) ?? throw new InvalidOperationException("LabelAxis did not start.");

        AutomationElement main = WaitFor(() => TopWindows(process.Id).FirstOrDefault(w => FindIn(w, "Menu.File") is not null),
            TimeSpan.FromSeconds(60), "the LabelAxis main window");
        return new AppDriver(process, data, main);
    }

    /// <summary>Windows of this LabelAxis open on the desktop (dialogs and message boxes too).</summary>
    public IReadOnlyList<AutomationElement> Windows() => TopWindows(Process.Id);

    private static List<AutomationElement> TopWindows(int processId) =>
        AutomationElement.RootElement
            .FindAll(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)))
            .Cast<AutomationElement>()
            .ToList();

    /// <summary>A window other than the main one, matched by its title (exact or containing).</summary>
    public AutomationElement WaitForWindow(Func<string, bool> title, string what) =>
        WaitFor(() => Windows().FirstOrDefault(w => !Equals(w, Main) && title(w.Current.Name)), Wait, what);

    /// <summary>A message box (LabelAxis's own, or the Windows one in English).</summary>
    public AutomationElement WaitForMessageBox() =>
        WaitFor(() => Windows().FirstOrDefault(w => !Equals(w, Main) &&
                                                    (w.Current.ClassName == "#32770" || FindIn(w, "MessageBox.OK") is not null ||
                                                     FindIn(w, "MessageBox.Yes") is not null)),
            Wait, "a message box");

    /// <summary>Presses a message-box button: OK, Yes, No or Cancel.</summary>
    public void Answer(AutomationElement box, string button)
    {
        string win32Id = button switch { "OK" => "1", "Cancel" => "2", "Yes" => "6", "No" => "7", _ => button };
        AutomationElement target = FindIn(box, "MessageBox." + button) ?? FindIn(box, win32Id)
                                   ?? throw new TestFailure($"The message box has no {button} button.");
        Click(target);
    }

    public AutomationElement Find(string automationId, AutomationElement? within = null) =>
        WaitFor(() => FindIn(within ?? Main, automationId), Wait, $"\"{automationId}\"");

    public static AutomationElement? FindIn(AutomationElement root, string automationId) =>
        root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    /// <summary>
    /// Presses a button or menu item, or ticks a box. Done from another thread: a button that
    /// opens a dialog only returns when the dialog closes, and the test has to work that dialog.
    /// </summary>
    public static void Click(AutomationElement element)
    {
        Exception? failure = null;
        var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object invoke))
                {
                    ((InvokePattern)invoke).Invoke();
                }
                else if (element.TryGetCurrentPattern(TogglePattern.Pattern, out object toggle))
                {
                    ((TogglePattern)toggle).Toggle();
                }
                else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object select))
                {
                    ((SelectionItemPattern)select).Select();
                }
                else
                {
                    throw new TestFailure($"\"{element.Current.Name}\" cannot be pressed.");
                }
            }
            catch (Exception ex) when (ex is not TestFailure)
            {
                failure = ex;
            }
            catch (TestFailure ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Long enough for a plain click to finish; a dialog keeps the call open, which is fine.
        done.Wait(TimeSpan.FromSeconds(2));
        if (failure is not null && failure is not ElementNotAvailableException)
        {
            throw new TestFailure($"Pressing \"{SafeName(element)}\" failed: {failure.Message}");
        }

        Thread.Sleep(300);
    }

    /// <summary>Opens a top-level menu and presses one of its items.</summary>
    public void Menu(string menuId, string itemId)
    {
        AutomationElement menu = Find(menuId);
        ((ExpandCollapsePattern)menu.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        AutomationElement item = WaitFor(() => FindIn(menu, itemId) ?? Windows().Select(w => FindIn(w, itemId)).FirstOrDefault(e => e is not null),
            Wait, $"menu item \"{itemId}\"");
        Click(item);
    }

    /// <summary>The rows of a list (pages, objects, templates).</summary>
    public static int Rows(AutomationElement list) =>
        list.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Count;

    public static void Type(AutomationElement box, string text)
    {
        if (!box.TryGetCurrentPattern(ValuePattern.Pattern, out object value))
        {
            throw new TestFailure($"\"{SafeName(box)}\" does not take text.");
        }

        ((ValuePattern)value).SetValue(text);
        Thread.Sleep(300);
    }

    public static void Close(AutomationElement window)
    {
        if (window.TryGetCurrentPattern(WindowPattern.Pattern, out object pattern))
        {
            ((WindowPattern)pattern).Close();
        }

        Thread.Sleep(300);
    }

    public static bool IsShown(AutomationElement element)
    {
        try
        {
            return !element.Current.IsOffscreen && element.Current.BoundingRectangle.Width > 0;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    public JsonObject ReadSettings() =>
        JsonNode.Parse(File.ReadAllText(SettingsFile))?.AsObject() ?? throw new TestFailure("settings.json is empty.");

    public static T WaitFor<T>(Func<T?> probe, TimeSpan timeout, string what)
        where T : class
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (probe() is { } found)
                {
                    return found;
                }
            }
            catch (ElementNotAvailableException)
            {
                // The window changed while it was being searched; look again.
            }

            if (clock.Elapsed > timeout)
            {
                throw new TestFailure($"Waited {timeout.TotalSeconds:0} s for {what}, which did not appear.");
            }

            Thread.Sleep(250);
        }
    }

    public static void WaitUntil(Func<bool> condition, TimeSpan timeout, string what) =>
        WaitFor(() => condition() ? "yes" : null, timeout, what);

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Current.AutomationId is { Length: > 0 } id ? id : element.Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return "(gone)";
        }
    }

    /// <summary>Closes LabelAxis (without saving) and removes the test's data folder.</summary>
    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        Process.Dispose();
        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A file still open; the temp folder is cleaned up by Windows later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A check that did not hold: the test fails with this message.</summary>
internal sealed class TestFailure(string message) : Exception(message);
