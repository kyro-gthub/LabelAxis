using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LabelMaker.App.Localization;
using LabelMaker.App.Services;
using LabelMaker.App.ViewModels;

namespace LabelMaker.App.Views;

/// <summary>
/// Add pages: blank ones, or copies of a chosen page (optionally with their barcodes counting up),
/// and how many. The pages go right after the page on screen.
/// </summary>
public partial class AddPagesWindow : Window
{
    /// <summary>The most pages added in one go.</summary>
    public const int MaxPages = 2000;

    private readonly bool _loading = true;

    public AddPagesWindow(IReadOnlyList<PageListItem> pages, int current)
    {
        InitializeComponent();
        SourceBox.ItemsSource = pages;
        SourceBox.SelectedIndex = Math.Clamp(current, 0, Math.Max(0, pages.Count - 1));
        _loading = false;
        Refresh();
        Loaded += (_, _) =>
        {
            CountBox.Focus();
            CountBox.SelectAll();
        };
    }

    /// <summary>What was chosen, or null when the window was cancelled.</summary>
    public AddPagesChoice? Choice { get; private set; }

    private int Count => int.TryParse(LabelMaker.Core.Localization.NativeDigits.ToAscii(CountBox.Text.Trim()), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnCountChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_loading)
        {
            return;
        }

        bool copies = CopyBox.IsChecked == true;
        SourceBox.IsEnabled = copies;
        CountUpBox.IsEnabled = copies;
        int count = Count;
        bool valid = count is >= 1 and <= MaxPages;
        AddButton.IsEnabled = valid && (!copies || SourceBox.SelectedItem is not null);
        CountHint.Text = valid ? string.Empty : Localizer.F("From 1 to {0}.", MaxPages);
        SummaryText.Text = !valid ? string.Empty
            : copies && SourceBox.SelectedItem is PageListItem page
                ? Localizer.F(CountUpBox.IsChecked == true ? "Adds {0} numbered copies of “{1}” after the page on screen." : "Adds {0} copies of “{1}” after the page on screen.", count, page.Name)
                : Localizer.F("Adds {0} blank page(s) after the page on screen.", count);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        bool copies = CopyBox.IsChecked == true;
        PageListItem? source = copies ? SourceBox.SelectedItem as PageListItem : null;
        Choice = new AddPagesChoice(Math.Clamp(Count, 1, MaxPages), !copies, source?.PageId, copies && CountUpBox.IsChecked == true);
        DialogResult = true;
    }
}
