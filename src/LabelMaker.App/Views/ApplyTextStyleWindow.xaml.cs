using System.Globalization;
using System.Windows;
using LabelMaker.App.Controls;
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

        // Other pages are ticked to start with; this page only matters for "every text box".
        Pages.Fill(session.Project, session.ActivePagePosition, CurrentPageRow.Optional, caption => caption + "  (this page)");
        Refresh();
    }

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

    private void Refresh()
    {
        // Each page row says how many text boxes on it would change.
        Pages.Describe((position, caption) =>
        {
            int count = Targets(new[] { position }).Count;
            return $"{caption}  ·  {(count == 1 ? "1 text box" : $"{count} text boxes")}";
        });

        int[] ticked = Pages.Ticked;
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
        int[] ticked = Pages.Ticked;
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
