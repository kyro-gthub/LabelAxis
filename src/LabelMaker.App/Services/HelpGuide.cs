using System.IO;
using System.Windows;
using System.Windows.Input;
using LabelMaker.App.Views;
using LabelMaker.Core.Help;

namespace LabelMaker.App.Services;

/// <summary>
/// The built-in help guide: its pages (embedded in the app), one Help window, and F1 in every
/// window. A window names its page with <c>svc:HelpGuide.Topic="printing"</c>; the main window
/// answers through <see cref="ContextTopic"/> with the page about what is selected.
/// </summary>
public static class HelpGuide
{
    public const string StartTopic = "getting-started";

    private static readonly Lazy<HelpLibrary> LazyLibrary = new(Load);
    private static HelpWindow? _window;

    public static readonly DependencyProperty TopicProperty = DependencyProperty.RegisterAttached(
        "Topic", typeof(string), typeof(HelpGuide), new PropertyMetadata(null));

    public static string? GetTopic(DependencyObject element) => (string?)element.GetValue(TopicProperty);

    public static void SetTopic(DependencyObject element, string? value) => element.SetValue(TopicProperty, value);

    /// <summary>For windows without a fixed page (the main window): the page about what is on screen.</summary>
    public static Func<Window, string?>? ContextTopic { get; set; }

    public static HelpLibrary Library => LazyLibrary.Value;

    /// <summary>Opens the guide (or brings it forward) at <paramref name="topicId"/>.</summary>
    public static void Show(Window? owner, string? topicId)
    {
        // Windows opened before a dialog are disabled while the dialog is up, so from inside a
        // dialog the guide opens afresh rather than bringing back one that cannot be used.
        if (_window is not null && System.Windows.Interop.ComponentDispatcher.IsThreadModal && !_window.OpenedDuringDialog)
        {
            _window.Close();
            _window = null;
        }

        if (_window is null)
        {
            _window = new HelpWindow(Library) { OpenedDuringDialog = System.Windows.Interop.ComponentDispatcher.IsThreadModal };
            _window.Closed += (_, _) => _window = null;
            if (owner is not null && owner.IsVisible)
            {
                _window.Left = owner.Left + Math.Max(0, owner.ActualWidth - 980) / 2;
                _window.Top = owner.Top + 40;
            }

            _window.Show();
        }

        _window.Navigate(Library.Find(topicId) ?? Library.Find(StartTopic) ?? Library.Topics.FirstOrDefault());
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    /// <summary>F1 in any window opens the guide at that window's page.</summary>
    public static void HookF1() =>
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler((sender, e) =>
        {
            if (e.Key != Key.F1 || Keyboard.Modifiers != ModifierKeys.None || sender is HelpWindow)
            {
                return;
            }

            var window = (Window)sender;
            Show(window, GetTopic(window) ?? ContextTopic?.Invoke(window));
            e.Handled = true;
        }));

    // English pages are "LabelAxis.Help.01-getting-started.md"; their translations
    // "LabelAxis.Help.ar.01-getting-started.md" (Help\ar, Help\ckb in the project).
    private const string Prefix = "LabelAxis.Help.";

    private static HelpLibrary Load()
    {
        var topics = new List<HelpTopic>();
        var assembly = typeof(HelpGuide).Assembly;
        string[] names = assembly.GetManifestResourceNames();
        string language = Localization.Localizer.Current.Code;
        foreach (string name in names.Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal) &&
                                                 !n[Prefix.Length..^3].Contains('.', StringComparison.Ordinal)))
        {
            // The page in the interface language when it has been translated, otherwise in English.
            string translated = Prefix + language + "." + name[Prefix.Length..];
            HelpTopic? topic = (language != "en" && names.Contains(translated) ? Read(assembly, translated) : null) ?? Read(assembly, name);
            if (topic is not null)
            {
                topics.Add(topic);
            }
        }

        return new HelpLibrary(topics);
    }

    private static HelpTopic? Read(System.Reflection.Assembly assembly, string name)
    {
        using Stream? stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        try
        {
            return HelpMarkup.Parse(reader.ReadToEnd());
        }
        catch (FormatException)
        {
            // A broken page is left out rather than taking the guide down.
            return null;
        }
    }
}
