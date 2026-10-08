using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LabelMaker.App.Controls;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.App.ViewModels;
using LabelMaker.Core.Serialization;

namespace LabelMaker.App.Views;

/// <summary>
/// The main window's page list: opening, moving and renaming pages in place, and the
/// click-outside rule that ends renaming in either list.
/// </summary>
public partial class MainWindow
{
    private void OnPageDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { IsSelected: true } && !IsInsideControl(e.OriginalSource as DependencyObject) && _vm.RenamePageCommand.CanExecute(null))
        {
            _vm.RenamePageCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>F2 renames the page; Ctrl+Up / Ctrl+Down move it. Plain arrows step through the pages.</summary>
    private void OnPageListKeyDown(object sender, KeyEventArgs e)
    {
        if (IsInsideControl(e.OriginalSource as DependencyObject))
        {
            return; // typing in the rename box
        }

        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        System.Windows.Input.ICommand? command = e.Key switch
        {
            Key.F2 when Keyboard.Modifiers == ModifierKeys.None => _vm.RenamePageCommand,
            Key.Up when ctrl => _vm.MovePageUpCommand,
            Key.Down when ctrl => _vm.MovePageDownCommand,
            _ => null,
        };

        if (command is not null)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true;
            if (e.Key != Key.F2)
            {
                FocusSelectedPage();
            }
        }
    }

    // ---- page rename in place ----

    private TextBox? _pageRenameBox;

    /// <summary>Shows a text box on the selected page's row, like renaming a file in Explorer.</summary>
    private void BeginPageRename() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        EndPageRename(commit: false);
        PageList.UpdateLayout();
        if (PageList.SelectedItem is not PageListItem page)
        {
            return;
        }

        PageList.ScrollIntoView(page);
        PageList.UpdateLayout();
        if (PageList.ItemContainerGenerator.ContainerFromItem(page) is not ListBoxItem row ||
            FindNamed<TextBox>(row, "PageRenameBox") is not { } box ||
            FindNamed<TextBlock>(row, "PageCaption") is not { } caption)
        {
            return;
        }

        box.Text = page.Name;
        caption.Visibility = Visibility.Hidden;
        box.Visibility = Visibility.Visible;
        _pageRenameBox = box;
        box.Focus();
        box.SelectAll();
    });

    private void EndPageRename(bool commit, bool refocus = false)
    {
        if (_pageRenameBox is not { } box)
        {
            return;
        }

        _pageRenameBox = null; // first, so losing focus below does not end the rename twice
        string text = box.Text;
        box.Visibility = Visibility.Collapsed;
        if (box.Parent is Panel panel)
        {
            foreach (TextBlock caption in panel.Children.OfType<TextBlock>())
            {
                caption.Visibility = Visibility.Visible;
            }
        }

        if (commit)
        {
            _vm.CommitPageRename(text);
        }

        // After Enter or Esc keep typing in the page list; after a click, focus goes where the user clicked.
        if (refocus)
        {
            FocusSelectedPage();
        }
    }

    private void OnPageRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            EndPageRename(commit: e.Key == Key.Enter, refocus: true);
            e.Handled = true;
        }
    }

    private void OnPageRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _pageRenameBox) || sender is not TextBox box)
        {
            return;
        }

        // Focus coming back to the row itself (a closing context menu, the second click of
        // the double-click) is not the user leaving the box.
        if (e.NewFocus is ListBoxItem row && box.IsDescendantOf(row))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (ReferenceEquals(_pageRenameBox, box))
                {
                    box.Focus();
                }
            });
            return;
        }

        // Only Enter keeps a new name; leaving the box any other way cancels.
        EndPageRename(commit: false);
    }

    /// <summary>A left click anywhere outside the rename box cancels renaming a page or an object.</summary>
    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (_pageRenameBox is { } pageBox && !IsWithin(source, pageBox))
        {
            EndPageRename(commit: false);
        }

        foreach (ObjectListItem row in _vm.ObjectList.Where(r => r.IsEditing).ToList())
        {
            if (!(FindAncestor<TextBox>(source) is { DataContext: var context } && ReferenceEquals(context, row)))
            {
                row.IsEditing = false;
            }
        }
    }

    private static bool IsWithin(DependencyObject? d, DependencyObject ancestor)
    {
        for (; d is not null; d = ParentOf(d))
        {
            if (ReferenceEquals(d, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d is not null; d = ParentOf(d))
        {
            if (d is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);

    /// <summary>True while a page or object name is being typed in its list.</summary>
    private bool IsRenaming => _pageRenameBox is not null || _vm.ObjectList.Any(r => r.IsEditing);

    private static T? FindNamed<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T { Name: var n } match && n == name)
            {
                return match;
            }

            if (FindNamed<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Keeps keyboard focus on the selected page row after the list changes under it.</summary>
    private void FocusSelectedPage() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
        if (PageList.SelectedItem is { } item && PageList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row)
        {
            row.Focus();
        }
    });
}
