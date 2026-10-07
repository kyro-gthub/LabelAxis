using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Automation;

namespace LabelMaker.UiTests;

/// <summary>Marks a click-through test; the text is what the report calls it.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ScenarioAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

/// <summary>
/// The click-through tests. Each starts its own LabelAxis with fresh settings, does what a
/// person would do, and checks what they would see.
/// </summary>
internal static class Scenarios
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new TestFailure(message);
        }
    }

    private static AutomationElement WindowWith(AppDriver app, string automationId, string what) =>
        AppDriver.WaitFor(() => app.Windows().FirstOrDefault(w => !Equals(w, app.Main) && AppDriver.FindIn(w, automationId) is not null),
            AppDriver.Wait, what);

    [Scenario("LabelAxis starts and shows its main window")]
    public static void Starts(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        Check(app.Main.Current.Name.Contains("LabelAxis", StringComparison.Ordinal), $"The window title is \"{app.Main.Current.Name}\".");
        app.Find("Tool.AddText");
        app.Find("ObjectListBox");
        app.Find("PageList");
    }

    [Scenario("Adding a text box puts it in the object list, and Undo takes it out again")]
    public static void AddTextAndUndo(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        AutomationElement list = app.Find("ObjectListBox");
        int before = AppDriver.Rows(list);

        AppDriver.Click(app.Find("Tool.AddText"));
        AppDriver.WaitUntil(() => AppDriver.Rows(list) == before + 1, AppDriver.Wait, "the new text box in the object list");

        app.Menu("Menu.Edit", "Menu.Undo");
        AppDriver.WaitUntil(() => AppDriver.Rows(list) == before, AppDriver.Wait, "Undo to remove the text box");
    }

    [Scenario("Adding a barcode, a QR code and a table puts each in the object list")]
    public static void AddObjects(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        AutomationElement list = app.Find("ObjectListBox");
        int before = AppDriver.Rows(list);
        foreach (string tool in new[] { "Tool.AddBarcode", "Tool.AddQrCode", "Tool.AddTable" })
        {
            AppDriver.Click(app.Find(tool));
        }

        AppDriver.WaitUntil(() => AppDriver.Rows(list) == before + 3, AppDriver.Wait, "three new objects in the object list");
    }

    [Scenario("Adding a page puts it in the page list, and Undo removes it")]
    public static void AddPage(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        AutomationElement pages = app.Find("PageList");
        int before = AppDriver.Rows(pages);

        AppDriver.Click(app.Find("Pages.Add"));
        AppDriver.WaitUntil(() => AppDriver.Rows(pages) == before + 1, AppDriver.Wait, "the new page in the page list");

        app.Menu("Menu.Edit", "Menu.Undo");
        AppDriver.WaitUntil(() => AppDriver.Rows(pages) == before, AppDriver.Wait, "Undo to remove the page");
    }

    [Scenario("Pages are numbered from 1, and Apply page format adds this page's objects to the other pages")]
    public static void PageFormat(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        AutomationElement pages = app.Find("PageList");
        AutomationElement objects = app.Find("ObjectListBox");
        int onFirst = AppDriver.Rows(objects);

        // A second page with a table, which the first page does not have.
        AppDriver.Click(app.Find("Pages.Add"));
        AppDriver.WaitUntil(() => AppDriver.Rows(pages) == 2, AppDriver.Wait, "the new page in the page list");
        AutomationElementCollection rows = pages.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        Check(rows[0].Current.Name.StartsWith("1.", StringComparison.Ordinal), $"The first page is shown as \"{rows[0].Current.Name}\", not numbered 1.");
        Check(rows[1].Current.Name.StartsWith("2.", StringComparison.Ordinal), $"The second page is shown as \"{rows[1].Current.Name}\", not numbered 2.");
        AppDriver.Click(app.Find("Tool.AddTable"));
        AppDriver.WaitUntil(() => AppDriver.Rows(objects) == 1, AppDriver.Wait, "the table on the new page");

        app.Menu("Menu.Edit", "Menu.PageFormat");
        AutomationElement window = WindowWith(app, "PageFormat.Apply", "the Apply page format window");
        AppDriver.Click(app.Find("PageFormat.AddMissing", window));
        AppDriver.Click(app.Find("PageFormat.Apply", window));
        AppDriver.WaitUntil(() => !app.Windows().Any(w => AppDriver.FindIn(w, "PageFormat.Apply") is not null), AppDriver.Wait, "the window to close");

        AppDriver.Click(rows[0]);
        AppDriver.WaitUntil(() => AppDriver.Rows(objects) == onFirst + 1, AppDriver.Wait, "the table copied onto page 1");
        app.Menu("Menu.Edit", "Menu.Undo");
        AppDriver.WaitUntil(() => AppDriver.Rows(objects) == onFirst, AppDriver.Wait, "Undo to take the copied table away");
    }

    [Scenario("Start from a design opens the first design as a new project with all its objects")]
    public static void StartFromDesign(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        AutomationElement objects = app.Find("ObjectListBox");

        app.Menu("Menu.File", "Menu.NewFromDesign");
        AutomationElement gallery = WindowWith(app, "Designs.Use", "the Start from a design window");
        AutomationElement list = app.Find("Designs.List", gallery);
        AppDriver.WaitUntil(() => AppDriver.Rows(list) >= 60, AppDriver.Wait, "the designs in the gallery");
        AppDriver.Click(app.Find("Designs.Use", gallery));
        AppDriver.WaitUntil(() => !app.Windows().Any(w => AppDriver.FindIn(w, "Designs.Use") is not null), AppDriver.Wait, "the gallery to close");

        // "Price tag – bold price": three words, a line, a price, a code and a date.
        AppDriver.WaitUntil(() => AppDriver.Rows(objects) == 6, AppDriver.Wait, "the design's 6 objects in the object list");
    }

    [Scenario("The New Project window offers a custom size")]
    public static void NewProjectCustomSize(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        app.Menu("Menu.File", "Menu.NewProject");
        AutomationElement window = WindowWith(app, "NewProject.CustomSize", "the New Project window");
        AppDriver.Click(app.Find("NewProject.CustomSize", window));
        AutomationElement custom = AppDriver.WaitFor(() => app.Windows().FirstOrDefault(w => w.Current.Name.Contains("custom", StringComparison.OrdinalIgnoreCase)),
            AppDriver.Wait, "the custom size window");
        AppDriver.Close(custom);
        AppDriver.Close(window);
    }

    [Scenario("The Print window opens and closes without printing")]
    public static void PrintWindow(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        app.Menu("Menu.Print", "Menu.PrintDialog");
        AutomationElement print = WindowWith(app, "PrintButton", "the Print window");
        app.Find("PrinterBox", print);
        app.Find("ThumbHost", print);
        AppDriver.Close(print);
        AppDriver.WaitUntil(() => !app.Windows().Any(w => AppDriver.FindIn(w, "PrintButton") is not null), AppDriver.Wait, "the Print window to close");
    }

    [Scenario("The Label manager finds a sheet by its code")]
    public static void LabelManager(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        app.Menu("Menu.File", "Menu.LabelManager");
        AutomationElement manager = WindowWith(app, "Categories", "the Label manager");
        AutomationElement list = app.Find("List", manager);
        AppDriver.WaitUntil(() => AppDriver.Rows(list) > 10, AppDriver.Wait, "the list of sheets");

        AppDriver.Type(app.Find("SearchBox", manager), "3100");
        AppDriver.WaitUntil(() => AppDriver.Rows(list) is > 0 and < 10, AppDriver.Wait, "the search to narrow the list");
        AppDriver.Close(manager);
    }

    [Scenario("Find and replace finds the text on the label")]
    public static void FindText(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        app.Menu("Menu.Edit", "Menu.FindReplace");
        AutomationElement find = WindowWith(app, "FindBox", "the Find and replace window");
        AppDriver.Type(app.Find("FindBox", find), "Product");
        AutomationElement results = app.Find("Results", find);
        AppDriver.WaitUntil(() => AppDriver.Rows(results) >= 1, AppDriver.Wait, "a match for \"Product\"");
        AppDriver.Close(find);
    }

    [Scenario("The help guide opens and its search finds pages")]
    public static void HelpGuide(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        app.Menu("Menu.Help", "Menu.HelpGuide");
        AutomationElement help = WindowWith(app, "TopicList", "the help guide");
        AppDriver.Type(app.Find("SearchBox", help), "calibration");
        AutomationElement topics = app.Find("TopicList", help);
        AppDriver.WaitUntil(() => AppDriver.Rows(topics) >= 1, AppDriver.Wait, "help pages about calibration");
        AppDriver.Close(help);
    }

    [Scenario("About shows the version")]
    public static void About(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        app.Menu("Menu.Help", "Menu.About");
        AutomationElement about = WindowWith(app, "VersionText", "the About window");
        string version = app.Find("VersionText", about).Current.Name;
        Check(version.Any(char.IsDigit), $"The version reads \"{version}\".");
        AppDriver.Close(about);
    }

    [Scenario("Choosing Arabic in Settings is saved, and LabelAxis offers to restart")]
    public static void ChooseArabic(string exe)
    {
        using AppDriver app = AppDriver.Start(exe);
        app.Menu("Menu.File", "Menu.Settings");
        AutomationElement settings = WindowWith(app, "Settings.Language", "the Settings window");

        AutomationElement combo = app.Find("Settings.Language", settings);
        ((ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        AutomationElement arabic = AppDriver.WaitFor(
            () => combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>().FirstOrDefault(i => i.Current.Name.Contains("Arabic", StringComparison.Ordinal)),
            AppDriver.Wait, "Arabic in the language list");
        AppDriver.Click(arabic);
        AppDriver.Click(app.Find("Settings.Save", settings));

        // "Restart LabelAxis now?" — not now, the test checks the saved setting instead.
        app.Answer(app.WaitForMessageBox(), "No");
        AppDriver.WaitUntil(() => (string?)app.ReadSettings()["Language"] == "ar", AppDriver.Wait, "\"Language\": \"ar\" in settings.json");
    }

    [Scenario("In Arabic the menus, windows and messages are in Arabic")]
    public static void Arabic(string exe) => InLanguage(exe, "ar", file: "ملف", settingsTitle: "الإعدادات", ok: "موافق");

    [Scenario("In Central Kurdish the menus, windows and messages are in Kurdish")]
    public static void Kurdish(string exe) => InLanguage(exe, "ckb", file: "پەڕگە", settingsTitle: "ڕێکخستنەکان", ok: "باشە");

    [Scenario("In French the menus, windows and messages are in French")]
    public static void French(string exe) => InLanguage(exe, "fr", file: "Fichier", settingsTitle: "Paramètres", ok: "OK");

    private static void InLanguage(string exe, string language, string file, string settingsTitle, string ok)
    {
        using AppDriver app = AppDriver.Start(exe, language);
        AppDriver.WaitUntil(() => app.Find("Menu.File").Current.Name == file, AppDriver.Wait, $"the File menu to read \"{file}\"");

        app.Menu("Menu.File", "Menu.Settings");
        AutomationElement settings = app.WaitForWindow(t => t == settingsTitle, $"the Settings window titled \"{settingsTitle}\"");
        AppDriver.Close(settings);

        // No update address is set, so Help › Check for updates explains that, in a LabelAxis message box.
        app.Menu("Menu.Help", "Menu.CheckForUpdates");
        AutomationElement box = app.WaitForMessageBox();
        AutomationElement button = app.Find("MessageBox.OK", box);
        Check(button.Current.Name == ok, $"The OK button reads \"{button.Current.Name}\", not \"{ok}\".");
        app.Answer(box, "OK");
    }

    [Scenario("A newer version at the update address is offered, and Later puts the offer away")]
    public static void UpdateOffered(string exe)
    {
        string feed = Path.Combine(Path.GetTempPath(), "LabelAxis-UiTests", "feed-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(feed, "update.json"), """
            {
              "version": "99.0.0",
              "notes": "A test version that does not exist.",
              "installer": { "file": "LabelAxis-Setup-99.0.0.exe", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "size": 1 }
            }
            """);
        try
        {
            using AppDriver app = AppDriver.Start(exe, settings: s =>
            {
                s["CheckForUpdates"] = true;
                s["UpdateAddress"] = feed;
            });

            // The daily check runs a few seconds after start-up.
            AutomationElement bar = AppDriver.WaitFor(() => AppDriver.FindIn(app.Main, "Main.UpdateBar"), TimeSpan.FromSeconds(60), "the update bar");
            Check(AppDriver.FindIn(bar, "Main.UpdateNow") is not null, "The update bar has no Update now button.");
            AppDriver.Click(app.Find("Main.UpdateLater", bar));
            AppDriver.WaitUntil(() => AppDriver.FindIn(app.Main, "Main.UpdateBar") is null, AppDriver.Wait, "the update bar to close");

            JsonObject saved = app.ReadSettings();
            Check(saved["LastUpdateCheckUtc"] is not null, "The time of the check was not saved.");
        }
        finally
        {
            Directory.Delete(feed, recursive: true);
        }
    }
}
