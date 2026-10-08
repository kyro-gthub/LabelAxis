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
/// The main window's list of objects on the page: selecting, dragging rows to restack, and
/// renaming in place.
/// </summary>
public partial class MainWindow
{
    private void OnObjectListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only the user's own clicks and keys; the rows the canvas highlights change this list too.
        if (sender is ListBox { IsKeyboardFocusWithin: true } list)
        {
            _vm.SelectObjectsFromList(list.SelectedItems.OfType<ObjectListItem>());
        }
    }

    // ---- objects on page: drag to reorder, rename ----

    private Point _objectDragStart;
    private ObjectListItem? _objectDragRow;
    private InsertionAdorner? _dropMarker;

    private static ObjectListItem? RowAt(ListBox list, DependencyObject? source) =>
        (source is null ? null : ItemsControl.ContainerFromElement(list, source)) is ListBoxItem { DataContext: ObjectListItem row } ? row : null;

    private void OnObjectListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _objectDragStart = e.GetPosition(null);
        _objectDragRow = sender is ListBox list && !IsInsideControl(e.OriginalSource as DependencyObject)
            ? RowAt(list, e.OriginalSource as DependencyObject)
            : null;

        // Pressing on one of several selected rows would collapse the selection before a drag
        // could take them all along; keep it, and pick just that row if the mouse comes up
        // without dragging.
        _pendingSingleSelect = null;
        if (sender is ListBox box && _objectDragRow is { IsSelected: true } row && e.ClickCount == 1 &&
            Keyboard.Modifiers == ModifierKeys.None && _vm.ObjectList.Count(r => r.IsSelected) > 1)
        {
            _pendingSingleSelect = row;
            box.Focus();
            e.Handled = true;
        }
    }

    private ObjectListItem? _pendingSingleSelect;

    private void OnObjectListMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingSingleSelect is { } row)
        {
            _pendingSingleSelect = null;
            _vm.SelectObjectsFromList(new[] { row });
        }
    }

    /// <summary>True inside a button or text box on a row, which keep their own mouse handling.</summary>
    private static bool IsInsideControl(DependencyObject? d)
    {
        for (; d is not null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase)
            {
                return true;
            }
        }

        return false;
    }

    private void OnObjectListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _objectDragRow is not { } row || sender is not ListBox list)
        {
            return;
        }

        Vector moved = e.GetPosition(null) - _objectDragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // Dragging a selected row takes the whole selection along; any other row goes alone.
        List<ObjectListItem> rows = row.IsSelected
            ? _vm.ObjectList.Where(r => r.IsSelected && !(r.IsGroupMember && _vm.ObjectList.Any(g => g.IsGroup && g.IsSelected && g.GroupId == r.GroupId))).ToList()
            : new List<ObjectListItem> { row };
        _objectDragRow = null;
        _pendingSingleSelect = null;
        try
        {
            DragDrop.DoDragDrop(list, new DataObject(typeof(ObjectRowsDrag), new ObjectRowsDrag(rows)), DragDropEffects.Move);
        }
        finally
        {
            ClearDropMarker();
        }
    }

    private sealed record ObjectRowsDrag(IReadOnlyList<ObjectListItem> Rows);

    private (ObjectListItem Row, bool Below, ListBoxItem Container)? DropTarget(ListBox list, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ObjectRowsDrag)) ||
            (e.OriginalSource is DependencyObject d ? ItemsControl.ContainerFromElement(list, d) : null) is not ListBoxItem { DataContext: ObjectListItem row } container)
        {
            // Below the last row: drop behind everything.
            if (e.Data.GetDataPresent(typeof(ObjectRowsDrag)) && list.Items.Count > 0 &&
                list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1) is ListBoxItem { DataContext: ObjectListItem last } lastContainer &&
                e.GetPosition(lastContainer).Y > lastContainer.ActualHeight)
            {
                return (last, true, lastContainer);
            }

            return null;
        }

        return (row, e.GetPosition(container).Y > container.ActualHeight / 2, container);
    }

    private void OnObjectListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (sender is ListBox list && DropTarget(list, e) is { } target)
        {
            e.Effects = DragDropEffects.Move;
            ShowDropMarker(target.Container, target.Below);
        }
        else
        {
            ClearDropMarker();
        }

        e.Handled = true;
    }

    private void OnObjectListDragLeave(object sender, DragEventArgs e) => ClearDropMarker();

    private void OnObjectListDrop(object sender, DragEventArgs e)
    {
        ClearDropMarker();
        if (sender is ListBox list && DropTarget(list, e) is { } target && e.Data.GetData(typeof(ObjectRowsDrag)) is ObjectRowsDrag drag)
        {
            _vm.MoveObjectsInList(drag.Rows, target.Row, target.Below);
            list.Focus();
        }

        e.Handled = true;
    }

    private void ShowDropMarker(ListBoxItem container, bool below)
    {
        if (_dropMarker?.AdornedElement == container)
        {
            _dropMarker.Below = below;
            return;
        }

        ClearDropMarker();
        if (System.Windows.Documents.AdornerLayer.GetAdornerLayer(container) is { } layer)
        {
            _dropMarker = new InsertionAdorner(container, (System.Windows.Media.Brush)FindResource("Br.Accent")) { Below = below };
            layer.Add(_dropMarker);
        }
    }

    private void ClearDropMarker()
    {
        if (_dropMarker is not null && System.Windows.Documents.AdornerLayer.GetAdornerLayer(_dropMarker.AdornedElement) is { } layer)
        {
            layer.Remove(_dropMarker);
        }

        _dropMarker = null;
    }

    /// <summary>The line showing where dragged rows will land.</summary>
    private sealed class InsertionAdorner : System.Windows.Documents.Adorner
    {
        private readonly System.Windows.Media.Pen _pen;
        private bool _below;

        public InsertionAdorner(UIElement adorned, System.Windows.Media.Brush brush) : base(adorned)
        {
            _pen = new System.Windows.Media.Pen(brush, 2);
            IsHitTestVisible = false;
        }

        public bool Below
        {
            get => _below;
            set
            {
                if (_below != value)
                {
                    _below = value;
                    InvalidateVisual();
                }
            }
        }

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            double y = _below ? AdornedElement.RenderSize.Height - 1 : 1;
            dc.DrawLine(_pen, new Point(2, y), new Point(AdornedElement.RenderSize.Width - 2, y));
        }
    }

    private void OnObjectRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: ObjectListItem row } && !IsInsideControl(e.OriginalSource as DependencyObject))
        {
            BeginRename(row);
            e.Handled = true;
        }
    }

    private void OnObjectRowRightClick(object sender, MouseButtonEventArgs e)
    {
        // Right-clicking an unselected row acts on that row, like the page list.
        if (sender is ListBoxItem { DataContext: ObjectListItem row } item && !row.IsSelected)
        {
            _vm.SelectObjectsFromList(new[] { row });
            item.Focus();
        }
    }

    private void OnRenameObjectMenu(object sender, RoutedEventArgs e)
    {
        if (_vm.ObjectList.FirstOrDefault(r => r.IsSelected && r.CanRename) is { } row)
        {
            BeginRename(row);
        }
    }

    private void OnObjectListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        System.Windows.Input.ICommand? command = e.Key switch
        {
            Key.Up when ctrl => _vm.BringForwardCommand,
            Key.Down when ctrl => _vm.SendBackwardCommand,
            _ => null,
        };

        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
        {
            OnRenameObjectMenu(sender, e);
            e.Handled = true;
        }
        else if (command is not null)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void BeginRename(ObjectListItem row)
    {
        if (!row.CanRename)
        {
            return;
        }

        foreach (ObjectListItem other in _vm.ObjectList.Where(r => r.IsEditing && r != row))
        {
            other.IsEditing = false;
        }

        row.IsEditing = true;
    }

    private void OnRenameBoxShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            box.Text = (box.DataContext as ObjectListItem)?.EditName ?? box.Text;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ObjectListItem row } box)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _vm.RenameObject(row, box.Text);
            ObjectListBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.IsEditing = false;
            ObjectListBox.Focus();
            e.Handled = true;
        }
    }

    private void OnRenameBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: ObjectListItem { IsEditing: true } row } box)
        {
            if (e.NewFocus is ListBoxItem item && box.IsDescendantOf(item))
            {
                // A closing context menu or the double-click handing focus back to the row.
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (row.IsEditing)
                    {
                        box.Focus();
                    }
                });
                return;
            }

            row.IsEditing = false; // only Enter keeps a new name
        }
    }
}
