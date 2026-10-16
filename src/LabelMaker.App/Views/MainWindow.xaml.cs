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
            // Passing over the canvas must not pull focus out of a rename box and cancel it, nor
            // out of another window: focusing an element brings its window to the front, so a
            // maximised main window would cover the Help guide or any other open window.
            if (IsActive && !_canvas.IsKeyboardFocusWithin && !IsRenaming)
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
        _autoSaveTimer.Tick += (_, _) => _vm.AutoSaveAll();
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

    private void OnCanvasContextMenuOpened(object? sender, RoutedEventArgs e) =>
        _removeGuideItem.Visibility = _canvas.TryFindGuideAtContext(out _, out _) ? Visibility.Visible : Visibility.Collapsed;

    public void OpenProjectPath(string path) => _vm.OpenProjectPath(path);

    public void RestoreRecovered(RecoveryInfo info) => _vm.RestoreRecovered(info);

    internal MainViewModel ViewModel => _vm;

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
