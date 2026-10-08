using System.Windows;
using System.Windows.Controls;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Controls;

/// <summary>How the page on screen is shown in a <see cref="PageChecklist"/>.</summary>
public enum CurrentPageRow
{
    /// <summary>Left out: the window has a box of its own for it.</summary>
    Hidden,

    /// <summary>Shown greyed out and unticked: it is the page things are copied from.</summary>
    Fixed,

    /// <summary>Shown unticked, and the user may tick it.</summary>
    Optional,
}

/// <summary>
/// The "Which pages" list of the windows that work across pages (Apply page format, Apply text
/// style, Remove from all pages): one tick box per page, numbered as in the Pages panel, with
/// Tick all and Untick all underneath. Each window says what to show after each page's name.
/// </summary>
public sealed class PageChecklist : UserControl
{
    private readonly StackPanel _rows = new();
    private readonly List<(CheckBox Box, int Position, string Caption)> _pages = new();

    public PageChecklist()
    {
        var tickAll = new Button { Content = "Tick all", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        var untickAll = new Button { Content = "Untick all", Padding = new Thickness(10, 3, 10, 3) };
        tickAll.SetResourceReference(StyleProperty, "GhostButton");
        untickAll.SetResourceReference(StyleProperty, "GhostButton");
        tickAll.Click += (_, e) => TickAll(true, e);
        untickAll.Click += (_, e) => TickAll(false, e);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(tickAll);
        buttons.Children.Add(untickAll);

        var layout = new StackPanel();
        layout.Children.Add(_rows);
        layout.Children.Add(buttons);
        Content = layout;
    }

    /// <summary>Raised when a page is ticked or unticked.</summary>
    public event RoutedEventHandler? Changed;

    /// <summary>The ticked pages the user may change, as Pages panel positions (0 = the master label).</summary>
    public int[] Ticked => _pages.Where(p => p.Box.IsChecked == true && p.Box.IsEnabled).Select(p => p.Position).ToArray();

    /// <summary>
    /// Lists every page of <paramref name="project"/>; the others start ticked. The page on
    /// screen (<paramref name="current"/>) is shown as <paramref name="currentRow"/> says, with
    /// its name as <paramref name="currentCaption"/> writes it.
    /// </summary>
    public void Fill(ProjectModel project, int current, CurrentPageRow currentRow, Func<string, string>? currentCaption = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _rows.Children.Clear();
        _pages.Clear();
        for (int i = 0; i <= project.Pages.Count; i++)
        {
            bool isCurrent = i == current;
            if (isCurrent && currentRow == CurrentPageRow.Hidden)
            {
                continue;
            }

            string caption = Caption(project, i);
            if (isCurrent && currentCaption is not null)
            {
                caption = currentCaption(caption);
            }

            var box = new CheckBox
            {
                IsChecked = !isCurrent,
                IsEnabled = !(isCurrent && currentRow == CurrentPageRow.Fixed),
                Margin = new Thickness(0, 3, 0, 3),
                Content = caption,
            };
            box.Click += (_, e) => Changed?.Invoke(this, e);
            _rows.Children.Add(box);
            _pages.Add((box, i, caption));
        }
    }

    /// <summary>
    /// Writes each page's line again: its name followed by what the window will do there.
    /// <paramref name="line"/> is given the page's position and its name; a greyed-out page
    /// shows only its name.
    /// </summary>
    public void Describe(Func<int, string, string> line)
    {
        ArgumentNullException.ThrowIfNull(line);
        foreach ((CheckBox box, int position, string caption) in _pages)
        {
            box.Content = box.IsEnabled ? line(position, caption) : caption;
        }
    }

    /// <summary>A page as the Pages panel names it: "1.  Master label", "2.  Page 2".</summary>
    public static string Caption(ProjectModel project, int position)
    {
        ArgumentNullException.ThrowIfNull(project);
        string name = position == 0 ? project.MasterLabel.Name : project.Pages[position - 1].Name;
        return $"{position + 1}.  {name}";
    }

    private void TickAll(bool ticked, RoutedEventArgs e)
    {
        foreach ((CheckBox box, _, _) in _pages.Where(p => p.Box.IsEnabled))
        {
            box.IsChecked = ticked;
        }

        Changed?.Invoke(this, e);
    }
}
