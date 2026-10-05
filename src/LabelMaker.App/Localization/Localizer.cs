using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using LabelMaker.Core.Localization;

namespace LabelMaker.App.Localization;

/// <summary>A language the interface can be shown in.</summary>
/// <param name="Code">The language code kept in the settings ("en", "ar", "ckb").</param>
/// <param name="NativeName">The language's name in itself, as the language list shows it.</param>
/// <param name="EnglishName">The language's name in English, shown beside it.</param>
public sealed record AppLanguage(string Code, string NativeName, string EnglishName, bool RightToLeft)
{
    public string Display => Code == "en" ? NativeName : $"{NativeName}  ·  {EnglishName}";

    public override string ToString() => Display;
}

/// <summary>
/// Shows the interface in the language chosen in Settings. The windows are written in English;
/// in another language every piece of text is swapped for its translation as it appears —
/// labels, buttons, menus, tooltips, window titles, and the messages the program puts together
/// while it runs — and the windows are laid out right to left for Arabic and Kurdish.
/// Labels being designed are never touched: the canvas draws them itself, and lists that show
/// the user's own data are marked <see cref="OffProperty"/>.
/// </summary>
public static class Localizer
{
    public static IReadOnlyList<AppLanguage> Languages { get; } = new[]
    {
        new AppLanguage("en", "English", "English", false),
        new AppLanguage("ar", "العربية", "Arabic", true),
        new AppLanguage("ckb", "کوردی (سۆرانی)", "Central Kurdish", true),
    };

    public static AppLanguage Current { get; private set; } = Languages[0];

    public static Translator Translator { get; private set; } = Translator.None;

    public static bool IsRightToLeft => Current.RightToLeft;

    public static FlowDirection FlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>The text in the interface language (English text stays as it is).</summary>
    public static string T(string? english) => Translator.Translate(english);

    /// <summary>A template in the interface language with its holes filled in.</summary>
    public static string F(string englishTemplate, params object?[] args) => Translator.Format(englishTemplate, args);

    /// <summary>
    /// The language for a setting: the one named, or for no setting the language Windows is shown
    /// in when it is one of these, otherwise English.
    /// </summary>
    public static AppLanguage Resolve(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
        }

        string windows = CultureInfo.CurrentUICulture.Name;
        if (windows.StartsWith("ckb", StringComparison.OrdinalIgnoreCase) || windows.StartsWith("ku-Arab", StringComparison.OrdinalIgnoreCase))
        {
            return Languages[2];
        }

        return windows.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? Languages[1] : Languages[0];
    }

    /// <summary>Reads a language's translations from the program (an empty list for English).</summary>
    public static Translator Load(AppLanguage language)
    {
        if (language.Code == "en")
        {
            return Translator.None;
        }

        using Stream? stream = typeof(Localizer).Assembly.GetManifestResourceStream($"LabelAxis.Languages.{language.Code}.json");
        if (stream is null)
        {
            return Translator.None;
        }

        using var reader = new StreamReader(stream);
        return Translator.FromJson(reader.ReadToEnd());
    }

    /// <summary>Switches the interface to <paramref name="language"/>. Call once, before the first window opens.</summary>
    public static void Start(AppLanguage language)
    {
        Current = language;
        Translator = Load(language);
        if (Translator.IsEmpty && !IsRightToLeft)
        {
            return;
        }

        // Loaded only reaches elements that have a Loaded handler of their own somewhere below
        // them, so most labels and menus would never hear it. Instead each window hands down an
        // inherited flag: every element takes it the moment it joins the window — all of them
        // when the window first lays itself out, and later the parts of templates, list rows,
        // tab pages and drop-downs as they are made.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler((sender, _) => Activate((FrameworkElement)sender)), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Activate((FrameworkElement)sender)), handledEventsToo: true);

        // Menus and tooltips open in a little window of their own.
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((sender, _) => Activate((FrameworkElement)sender)), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent,
            new RoutedEventHandler((sender, _) => Activate((FrameworkElement)sender)), handledEventsToo: true);
    }

    // Inherited by everything inside a window once the window has it; see Start.
    private static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(Localizer),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnActiveChanged));

    private static void Activate(FrameworkElement root)
    {
        if (root.ReadLocalValue(ActiveProperty) is not true)
        {
            root.SetValue(ActiveProperty, true);
        }

        // Already inheriting it (a menu inside a window): it still needs its own direction.
        Apply(root);
    }

    private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && d is FrameworkElement element)
        {
            Apply(element);
        }
    }

    // Off is inherited too, but while an element is joining a window its inherited values are
    // still being brought up to date, so the setting is looked for on the element and above it.
    private static bool IsOff(DependencyObject d)
    {
        for (DependencyObject? node = d; node is not null; node = InheritanceParent(node))
        {
            if (node.ReadLocalValue(OffProperty) is true)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? InheritanceParent(DependencyObject node) => node switch
    {
        FrameworkElement { Parent: { } logical } => logical,
        FrameworkContentElement { Parent: { } logical } => logical,
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(node),
        _ => null,
    };

    /// <summary>
    /// Set on a list or panel that shows the user's own text (records, search results, file
    /// names): nothing inside it is translated.
    /// </summary>
    public static readonly DependencyProperty OffProperty = DependencyProperty.RegisterAttached(
        "Off", typeof(bool), typeof(Localizer),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetOff(DependencyObject d) => (bool)d.GetValue(OffProperty);

    public static void SetOff(DependencyObject d, bool value) => d.SetValue(OffProperty, value);

    // Mirrors the watched text property, so a change to it (from a binding, a template or code)
    // can be translated as it happens. A binding on the element itself holds no outside
    // reference, so nothing is kept alive after the element goes.
    private static readonly DependencyProperty WatchedProperty = DependencyProperty.RegisterAttached(
        "Watched", typeof(object), typeof(Localizer), new PropertyMetadata(null, OnWatchedChanged));

    /// <summary>
    /// Translates everything inside <paramref name="root"/> at once, as if each part had just
    /// appeared — for pictures of a window drawn off-screen, which never raise Loaded.
    /// </summary>
    public static void ApplyTo(DependencyObject root)
    {
        if (root is FrameworkElement element)
        {
            Apply(element);
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            ApplyTo(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static void Apply(object sender)
    {
        // The labels themselves, not only what is drawn: screen readers (and the click-through
        // tests) read a button's content, a menu's header and a tooltip, not the text inside.
        if (sender is FrameworkElement element && !Translator.IsEmpty && !IsOff(element))
        {
            TranslateLocal(element, FrameworkElement.ToolTipProperty);
            if (element is HeaderedItemsControl)
            {
                TranslateLocal(element, HeaderedItemsControl.HeaderProperty);
            }
            else if (element is HeaderedContentControl)
            {
                TranslateLocal(element, HeaderedContentControl.HeaderProperty);
            }

            // Not a list row made for a string in the user's data (its DataContext is that string).
            if (element is ContentControl { Content: string content } control && !Equals(control.DataContext, content))
            {
                TranslateLocal(element, ContentControl.ContentProperty);
            }
        }

        switch (sender)
        {
            case Window window:
                if (IsRightToLeft)
                {
                    window.FlowDirection = FlowDirection.RightToLeft;
                }

                Watch(window, Window.TitleProperty);
                break;

            case TextBlock text:
                Attach(text);
                break;

            case AccessText access when !IsOff(access):
                Watch(access, AccessText.TextProperty);
                break;

            // Pictures, icons and drawings (sheet previews, crop frames) keep their own left and
            // right; in a right-to-left window a Canvas would place its children from the right.
            case Image or Shape or Canvas when IsRightToLeft &&
                                               ((FrameworkElement)sender).ReadLocalValue(FrameworkElement.FlowDirectionProperty) == DependencyProperty.UnsetValue:
                ((FrameworkElement)sender).FlowDirection = FlowDirection.LeftToRight;
                break;

            // Menus and tooltips open in their own little window; they read right to left too
            // (also the menus of the canvas, which itself stays left to right).
            case ContextMenu or ToolTip when IsRightToLeft:
                ((FrameworkElement)sender).FlowDirection = FlowDirection.RightToLeft;
                break;
        }
    }

    // A text written in the window (not bound to data) is replaced by its translation.
    private static void TranslateLocal(DependencyObject d, DependencyProperty property)
    {
        if (d.GetValue(property) is string text && text.Length > 0 && !BindingOperations.IsDataBound(d, property))
        {
            string translated = T(text);
            if (!string.Equals(translated, text, StringComparison.Ordinal))
            {
                d.SetCurrentValue(property, translated);
            }
        }
    }

    private static void Attach(TextBlock text)
    {
        if (VisualTreeHelper.GetParent(text) is AccessText)
        {
            return;
        }

        if (IsInRecordsTable(text))
        {
            text.SetValue(OffProperty, true);
        }

        // The user's own text is not translated, but in a right-to-left window it still has to
        // keep its own order ("Apples (red)" must not show as "(Apples (red").
        if (IsOff(text))
        {
            if (IsRightToLeft)
            {
                Watch(text, TextBlock.TextProperty);
            }

            return;
        }

        // A TextBlock written with Text= (or filled by a template, or empty until code fills it)
        // is watched as a whole; one made of several runs ("Label" + "Axis", a bold word) is
        // translated run by run.
        if (DependencyPropertyHelper.GetValueSource(text, TextBlock.TextProperty).BaseValueSource != BaseValueSource.Default ||
            text.Inlines.Count == 0)
        {
            Watch(text, TextBlock.TextProperty);
            return;
        }

        // A list first: changing a run's text changes the collection being walked.
        foreach (Run run in Runs(text.Inlines).ToList())
        {
            if (IsOff(run))
            {
                continue;
            }

            if (BindingOperations.IsDataBound(run, Run.TextProperty))
            {
                Watch(run, Run.TextProperty);
            }
            else
            {
                string translated = T(run.Text);
                if (!string.Equals(translated, run.Text, StringComparison.Ordinal))
                {
                    run.Text = translated;
                }
            }
        }
    }

    private static IEnumerable<Run> Runs(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            if (inline is Run run)
            {
                yield return run;
            }
            else if (inline is Span span)
            {
                foreach (Run inner in Runs(span.Inlines))
                {
                    yield return inner;
                }
            }
        }
    }

    // The cells of a records table hold the user's data.
    private static bool IsInRecordsTable(TextBlock text)
    {
        DependencyObject? node = VisualTreeHelper.GetParent(text);
        for (int depth = 0; node is not null && depth < 12; depth++)
        {
            if (node is DataGridCell)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private static void Watch(DependencyObject element, DependencyProperty property)
    {
        if ((Translator.IsEmpty && !IsRightToLeft) || BindingOperations.IsDataBound(element, WatchedProperty))
        {
            return;
        }

        BindingOperations.SetBinding(element, WatchedProperty, new Binding
        {
            Source = element,
            Path = new PropertyPath(property),
            Mode = BindingMode.OneWay,
        });
    }

    private static void OnWatchedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string text || text.Length == 0 || text[0] == LeftToRightMark)
        {
            return;
        }

        string translated = IsOff(d) ? text : T(text);
        if (d is not Window)
        {
            translated = KeepOwnOrder(translated);
        }

        if (string.Equals(translated, text, StringComparison.Ordinal))
        {
            return;
        }

        DependencyProperty? target = d switch
        {
            TextBlock => TextBlock.TextProperty,
            AccessText => AccessText.TextProperty,
            Run => Run.TextProperty,
            Window => Window.TitleProperty,
            _ => null,
        };

        // SetCurrentValue keeps the binding or template that set the English text, so the next
        // change still comes through (and is translated in its turn).
        if (target is not null)
        {
            d.SetCurrentValue(target, translated);
        }
    }

    // WPF ignores the embedding marks (U+202A…U+202E) but honours the left-to-right mark.
    private const char LeftToRightMark = (char)0x200E;

    /// <summary>
    /// In a right-to-left window, text that starts in English (a printer or sheet name, a
    /// technical term left in English) is marked as left to right, so its brackets and full stop
    /// stay where they belong. A button that is just an arrow points the other way, as its
    /// place in the row has turned round.
    /// </summary>
    private static string KeepOwnOrder(string text)
    {
        if (!IsRightToLeft || text.Length == 0)
        {
            return text;
        }

        if (text.Length <= 3 && text.All(c => Arrows.ContainsKey(c) || c == ' '))
        {
            // Marked like English text, so it is turned round once and not back again.
            return LeftToRightMark + new string(text.Select(c => Arrows.TryGetValue(c, out char m) ? m : c).ToArray()) + LeftToRightMark;
        }

        return !LabelMaker.Core.Rendering.Bidi.HasRightToLeft(text) && LabelMaker.Core.Rendering.Bidi.FirstStrong(text) == LabelMaker.Core.Rendering.TextFlow.LeftToRight
            ? LeftToRightMark + text + LeftToRightMark
            : text;
    }

    private static readonly Dictionary<char, char> Arrows = new()
    {
        ['◀'] = '▶', ['▶'] = '◀', ['◁'] = '▷', ['▷'] = '◁', ['⏮'] = '⏭', ['⏭'] = '⏮',
        ['⏪'] = '⏩', ['⏩'] = '⏪', ['←'] = '→', ['→'] = '←', ['⟵'] = '⟶', ['⟶'] = '⟵',
    };

    /// <summary>
    /// A file-dialog filter ("Images|*.png;*.jpg|All files|*.*") with its descriptions translated
    /// and its patterns left alone.
    /// </summary>
    public static string Filter(string filter)
    {
        if (Translator.IsEmpty)
        {
            return filter;
        }

        string[] parts = filter.Split('|');
        for (int i = 0; i < parts.Length; i += 2)
        {
            parts[i] = T(parts[i]);
        }

        return string.Join('|', parts);
    }
}
