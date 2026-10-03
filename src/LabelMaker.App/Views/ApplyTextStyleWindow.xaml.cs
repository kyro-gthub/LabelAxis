using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Services;
using LabelMaker.Core.Editing;
using LabelMaker.Core.Model;
using LabelMaker.Core.Model.Objects;
using LabelMaker.Core.Printing;

namespace LabelMaker.App.Views;

/// <summary>
/// Gives the selected text box's font, size, colour and so on to text boxes on other pages,
/// either the box with the same name on each page or every text box there, in one undo step.
/// </summary>
public partial class ApplyTextStyleWindow : Window
{
    private readonly DocumentSession _session;
    private readonly TextObject _source;
    private readonly List<(CheckBox Box, int Position, string Caption)> _pages = new();

    public ApplyTextStyleWindow(DocumentSession session, TextObject source)
    {
        _session = session;
        _source = source;
        InitializeComponent();

        string name = string.IsNullOrWhiteSpace(source.Name) ? "this text box" : $"“{source.Name.Trim()}”";
        SourceText.Text = $"{name}: {Describe(source)}";
        if (string.IsNullOrWhiteSpace(source.Name))
        {
            SameNameBox.Content = "Text boxes with the same name (this one has no name)";
            SameNameBox.IsEnabled = false;
            EveryTextBox.IsChecked = true;
        }
        else
        {
            SameNameBox.Content = $"Only the text box called “{source.Name.Trim()}” on each page";
        }

        int current = CurrentPosition();
        ProjectModel project = session.Project;
        for (int i = 0; i <= project.Pages.Count; i++)
        {
            string caption = i == 0 ? $"★  {project.MasterLabel.Name}" : $"{i}.  {project.Pages[i - 1].Name}";
            if (i == current)
            {
                caption += "  (this page)";
            }

            // Other pages are ticked to start with; this page only matters for "every text box".
            var box = new CheckBox { IsChecked = i != current, Margin = new System.Windows.Thickness(0, 3, 0, 3) };
            box.Click += OnChanged;
            PageList.Children.Add(box);
            _pages.Add((box, i, caption));
        }

        Refresh();
    }

    private int CurrentPosition() => _session.IsEditingMaster
        ? 0
        : _session.Project.Pages.FindIndex(p => p.Id == _session.ActivePageId) + 1;

    private TextStyleParts Parts =>
        (FontBox.IsChecked == true ? TextStyleParts.Font : 0) |
        (SizeBox.IsChecked == true ? TextStyleParts.Size : 0) |
        (StyleBox.IsChecked == true ? TextStyleParts.Style : 0) |
        (ColourBox.IsChecked == true ? TextStyleParts.Colour : 0) |
        (AlignmentBox.IsChecked == true ? TextStyleParts.Alignment : 0) |
        (SpacingBox.IsChecked == true ? TextStyleParts.Spacing : 0);

    private bool SameNameOnly => SameNameBox.IsChecked == true;

    private IReadOnlyList<TextObject> Targets(IEnumerable<int> positions) =>
        TextStyleCopy.Targets(_session.Project, _source, positions, SameNameOnly);

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnTickAll(object sender, RoutedEventArgs e) => TickAll(true);

    private void OnUntickAll(object sender, RoutedEventArgs e) => TickAll(false);

    private void TickAll(bool ticked)
    {
        foreach ((CheckBox box, _, _) in _pages)
        {
            box.IsChecked = ticked;
        }

        Refresh();
    }

    private void Refresh()
    {
        // Each page row says how many text boxes on it would change.
        foreach ((CheckBox box, int position, string caption) in _pages)
        {
            int count = Targets(new[] { position }).Count;
            box.Content = $"{caption}  ·  {(count == 1 ? "1 text box" : $"{count} text boxes")}";
        }

        int[] ticked = _pages.Where(p => p.Box.IsChecked == true).Select(p => p.Position).ToArray();
        int targets = Targets(ticked).Count;
        int pages = ticked.Count(p => Targets(new[] { p }).Count > 0);

        if (Parts == TextStyleParts.None)
        {
            SummaryText.Text = "Tick at least one thing to copy.";
            ApplyButton.IsEnabled = false;
        }
        else if (targets == 0)
        {
            SummaryText.Text = SameNameOnly
                ? "No text box with this name on the ticked pages."
                : "No text boxes on the ticked pages.";
            ApplyButton.IsEnabled = false;
        }
        else
        {
            SummaryText.Text = $"Changes {targets} text box(es) on {pages} page(s). Undo puts them back.";
            ApplyButton.IsEnabled = true;
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        int[] ticked = _pages.Where(p => p.Box.IsChecked == true).Select(p => p.Position).ToArray();
        IUndoableCommand? command = TextStyleCopy.Build(_source, Targets(ticked), Parts, _session.TextMeasurer);
        if (command is not null)
        {
            _session.Execute(command);
        }

        DialogResult = true;
    }

    private static string Describe(TextObject t)
    {
        var parts = new List<string>
        {
            t.FontFamily,
            t.FontSizePt.ToString("0.#", CultureInfo.InvariantCulture) + " pt",
        };
        if (t.Bold)
        {
            parts.Add("bold");
        }

        if (t.Italic)
        {
            parts.Add("italic");
        }

        if (t.Underline)
        {
            parts.Add("underlined");
        }

        if (t.Strikethrough)
        {
            parts.Add("struck through");
        }

        parts.Add("colour " + t.Foreground.ToHex());
        return string.Join(", ", parts);
    }
}
