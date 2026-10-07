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

public partial class MainWindow : Window
{
    private readonly AppEnvironment _env;
    private readonly MainViewModel _vm;
    private readonly DesignerCanvas _canvas;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly InlineTextEditor _textEditor;
    private MenuItem _removeGuideItem = null!;

    public MainWindow(AppEnvironment env)
    {
        _env = env;
        InitializeComponent();
        MaximiseFix.Attach(this, Root);

        // Print requests from other programs (watch folder, web link), when switched on in Settings.
        env.Automation = new Automation.AutomationHost(env, Dispatcher);
        Loaded += async (_, _) =>
        {
            if (env.Automation is { } automation)
            {
                await automation.ApplyAsync();
            }
        };
        StateChanged += (_, _) => UpdateMaximiseButton();
        AllowDrop = true;

        var shell = new DialogService(this, env);
        _vm = new MainViewModel(env, shell);
        shell.Bind(_vm);
        _vm.PageRenameRequested += (_, _) => BeginPageRename();
        DataContext = _vm;
        HelpGuide.ContextTopic = _ => _vm.HelpTopicForSelection();

        _canvas = new DesignerCanvas(_vm.SheetRenderer) { CornerRadius = 17 };
        _canvas.ContextMenu = BuildCanvasContextMenu();
        _canvas.ContextMenu.Opened += OnCanvasContextMenuOpened;

        // The canvas draws everything itself; real controls (the in-place text
        // editor) live in a transparent overlay stacked on top of it.
        var overlay = new Canvas { ClipToBounds = true };
        var stack = new Grid();
        stack.Children.Add(_canvas);
        stack.Children.Add(overlay);
        CanvasHost.Child = WithScrollBars(stack);
        _textEditor = new InlineTextEditor(_canvas, overlay, () => _vm.Session);
        _vm.AttachCanvas(_canvas);

        // Text being typed on the canvas belongs to the tab it was started in.
        _vm.ActiveTabChanging += (_, _) =>
        {
            if (_textEditor.IsEditing)
            {
                _textEditor.Commit();
            }
        };
        Loaded += (_, _) => _canvas.Focus();
        PreviewMouseLeftButtonDown += OnWindowPreviewMouseDown;
        _canvas.MouseEnter += (_, _) =>
        {
            // Passing over the canvas must not pull focus out of a rename box and cancel it.
            if (!_canvas.IsKeyboardFocusWithin && !IsRenaming)
            {
                _canvas.Focus();
            }
        };

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Title))
            {
                Title = _vm.Title;
            }
        };
        Title = _vm.Title;

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(30, env.Settings.AutoSaveSeconds)) };
        _autoSaveTimer.Tick += (_, _) => TryAutoSave();
        if (env.Settings.AutoSaveEnabled)
        {
            _autoSaveTimer.Start();
        }

        _vm.AutoSaveSettingsChanged += (_, _) =>
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(30, env.Settings.AutoSaveSeconds));
            if (env.Settings.AutoSaveEnabled)
            {
                _autoSaveTimer.Start();
            }
        };
    }

    /// <summary>
    /// Scroll bars along the right and bottom of the canvas, to move around a zoomed-in
    /// label. They follow the canvas's own pan and zoom, and dragging them pans it.
    /// </summary>
    private UIElement WithScrollBars(UIElement content)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var vertical = new System.Windows.Controls.Primitives.ScrollBar
        {
            Orientation = Orientation.Vertical, Width = 14, SmallChange = 24, ToolTip = "Scroll up and down (Ctrl + mouse wheel)",
        };
        var horizontal = new System.Windows.Controls.Primitives.ScrollBar
        {
            Orientation = Orientation.Horizontal, Height = 14, Width = double.NaN, SmallChange = 24, ToolTip = "Scroll left and right (Shift + mouse wheel)",
        };
        vertical.SetResourceReference(Control.BackgroundProperty, "Br.Surface2");
        horizontal.SetResourceReference(Control.BackgroundProperty, "Br.Surface2");

        Grid.SetColumn(vertical, 1);
        Grid.SetRow(horizontal, 1);
        var corner = new Border();
        corner.SetResourceReference(Border.BackgroundProperty, "Br.Surface2");
        Grid.SetColumn(corner, 1);
        Grid.SetRow(corner, 1);

        grid.Children.Add(content);
        grid.Children.Add(vertical);
        grid.Children.Add(horizontal);
        grid.Children.Add(corner);

        static void Apply(System.Windows.Controls.Primitives.ScrollBar bar, DesignerCanvas.ScrollInfo info)
        {
            bar.Minimum = info.Minimum;
            bar.Maximum = info.Maximum;
            bar.ViewportSize = info.Viewport;
            bar.LargeChange = Math.Max(24, info.Viewport * 0.8);
            bar.Value = info.Value;
        }

        _canvas.ScrollChanged += (_, _) =>
        {
            Apply(horizontal, _canvas.HorizontalScroll);
            Apply(vertical, _canvas.VerticalScroll);
        };
        horizontal.Scroll += (_, e) => _canvas.ScrollTo(horizontal: true, e.NewValue);
        vertical.Scroll += (_, e) => _canvas.ScrollTo(horizontal: false, e.NewValue);
        return grid;
    }

    private ContextMenu BuildCanvasContextMenu()
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, System.Windows.Input.ICommand command, string? gesture = null)
        {
            var mi = new MenuItem { Header = header, Command = command };
            if (gesture is not null)
            {
                mi.InputGestureText = gesture;
            }

            return mi;
        }

        menu.Items.Add(Item("Cu_t", _vm.CutCommand, "Ctrl+X"));
        menu.Items.Add(Item("_Copy", _vm.CopyCommand, "Ctrl+C"));
        menu.Items.Add(Item("_Paste here", _vm.PasteHereCommand, "Ctrl+V"));
        menu.Items.Add(Item("_Duplicate", _vm.DuplicateCommand, "Ctrl+D"));
        menu.Items.Add(Item("De_lete", _vm.DeleteCommand, "Del"));
        menu.Items.Add(Item("Remove from all _pages…", _vm.RemoveFromAllPagesCommand));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Bring to _front", _vm.BringToFrontCommand));
        menu.Items.Add(Item("Send to _back", _vm.SendToBackCommand));
        menu.Items.Add(Item("_Group", _vm.GroupCommand, "Ctrl+G"));
        menu.Items.Add(Item("_Ungroup", _vm.UngroupCommand, "Ctrl+Shift+G"));
        menu.Items.Add(new Separator());

        var guides = new MenuItem { Header = "Add _guide" };
        guides.Items.Add(Item("_Vertical guide here", _vm.AddVerticalGuideCommand));
        guides.Items.Add(Item("_Horizontal guide here", _vm.AddHorizontalGuideCommand));
        guides.Items.Add(new Separator());
        guides.Items.Add(Item("_Clear guides", _vm.ClearGuidesCommand));
        menu.Items.Add(guides);

        _removeGuideItem = new MenuItem { Header = "_Remove this guide", Visibility = Visibility.Collapsed };
        _removeGuideItem.Click += (_, _) =>
        {
            if (_canvas.TryFindGuideAtContext(out bool vertical, out int index))
            {
                _canvas.RemoveGuideAt(vertical, index);
            }
        };
        menu.Items.Insert(menu.Items.IndexOf(guides) + 1, _removeGuideItem);

        menu.Items.Add(Item("_Select all", new RelayCommand(() =>
            _vm.Session.SetSelection(_vm.Session.ActiveLabel.Objects))));

        return menu;
    }

    // ---- shell chrome ----------------------------------------------------

    /// <summary>The theme menu of the title bar: each theme with its swatch; a click switches at once.</summary>
    private void OnThemeMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ThemeButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var rowTemplate = (DataTemplate)FindResource("ThemeRow");
        foreach (Services.ThemeInfo theme in Services.ThemeManager.Themes)
        {
            var item = new MenuItem
            {
                Header = theme,
                HeaderTemplate = rowTemplate,
                IsCheckable = true,
                IsChecked = theme.Id == Services.ThemeManager.CurrentId,
                MinWidth = 220,
            };
            item.Click += (_, _) => _vm.ApplyTheme(theme.Id);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var own = new MenuItem { Header = Services.ThemeManager.Custom is null ? "Make your own theme…" : "Change your own theme…" };
        own.Click += (_, _) => _vm.EditCustomTheme();
        menu.Items.Add(own);

        menu.IsOpen = true;
    }

    private void OnMinimise(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximise(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>The middle caption button says what it will do: maximise, or restore down.</summary>
    private void UpdateMaximiseButton()
    {
        bool maximised = WindowState == WindowState.Maximized;
        string name = maximised ? "Restore down" : "Maximise";
        MaxButton.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(MaxButton, name);
        MaxGlyph.Data = System.Windows.Media.Geometry.Parse(maximised
            ? "M 2,0 L 10,0 L 10,8 M 0,2 L 8,2 L 8,10 L 0,10 Z"
            : "M 0,0 L 9,0 L 9,9 L 0,9 Z");
    }

    /// <summary>Middle-click closes a document tab, as in browsers.</summary>
    private void OnTabMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: DocumentTab tab })
        {
            _vm.CloseTab(tab);
            e.Handled = true;
        }
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is FrameworkElement { DataContext: DocumentTab tab })
        {
            _vm.ActivateTab(tab);
        }
    }

    /// <summary>Dropping .labelproj files on the window opens each in its own tab.</summary>
    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            string[] projects = files.Where(f => f.EndsWith(".labelproj", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (projects.Length > 0)
            {
                foreach (string file in projects)
                {
                    _vm.OpenProjectPath(file);
                }

                e.Handled = true;
                return;
            }
        }

        base.OnDrop(e);
    }

    /// <summary>Drops the Align / Arrange split-button menu under the button that was clicked.</summary>
    private void OnAlignMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    /// <summary>Right-clicking a list row should select it first, so the context menu acts on what was clicked.</summary>
    private void OnListItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

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

    private void OnCanvasContextMenuOpened(object? sender, RoutedEventArgs e) =>
        _removeGuideItem.Visibility = _canvas.TryFindGuideAtContext(out _, out _) ? Visibility.Visible : Visibility.Collapsed;

    public void OpenProjectPath(string path) => _vm.OpenProjectPath(path);

    public void RestoreRecovered(RecoveryInfo info) => _vm.RestoreRecovered(info);

    internal MainViewModel ViewModel => _vm;

    private void TryAutoSave()
    {
        try
        {
            int saved = 0;
            foreach (DocumentTab tab in _vm.Tabs.Where(t => t.HasUnsavedChanges))
            {
                tab.Session.SyncDataCache();
                _env.AutoSave.Snapshot(tab.Session.Document);
                saved++;
            }

            if (saved > 0)
            {
                _vm.StatusText = saved == 1
                    ? $"Auto-saved recovery snapshot at {DateTime.Now:T}"
                    : $"Auto-saved {saved} projects at {DateTime.Now:T}";
            }
        }
        catch (Exception)
        {
            // recovery snapshots are best-effort
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_textEditor.IsEditing)
        {
            _textEditor.Commit();
        }

        if (!_vm.ConfirmCloseAll())
        {
            e.Cancel = true;
            return;
        }

        _autoSaveTimer.Stop();
        foreach (DocumentTab tab in _vm.Tabs)
        {
            _env.AutoSave.ClearFor(tab.Session.Project.Metadata.Id);
        }

        _env.SaveSettings();
        if (_env.Automation is { } automation)
        {
            _env.Automation = null;
            _ = automation.DisposeAsync().AsTask();
        }

        base.OnClosing(e);
    }
}
