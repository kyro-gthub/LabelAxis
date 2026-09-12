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
    private MenuItem _removeGuideItem = null!;

    public MainWindow(AppEnvironment env)
    {
        _env = env;
        InitializeComponent();
        MaximiseFix.Attach(this);

        var shell = new DialogService(this, env);
        _vm = new MainViewModel(env, shell);
        shell.Bind(_vm);
        DataContext = _vm;

        _canvas = new DesignerCanvas(_vm.SheetRenderer);
        _canvas.ContextMenu = BuildCanvasContextMenu();
        _canvas.ContextMenu.Opened += OnCanvasContextMenuOpened;
        CanvasHost.Child = _canvas;
        _vm.AttachCanvas(_canvas);
        Loaded += (_, _) => _canvas.Focus();
        _canvas.MouseEnter += (_, _) =>
        {
            if (!_canvas.IsKeyboardFocusWithin)
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
        menu.Items.Add(Item("_Paste here", _vm.PasteCommand, "Ctrl+V"));
        menu.Items.Add(Item("_Duplicate", _vm.DuplicateCommand, "Ctrl+D"));
        menu.Items.Add(Item("De_lete", _vm.DeleteCommand, "Del"));
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

    private void OnMinimise(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximise(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

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

    private void TryAutoSave()
    {
        try
        {
            if (_vm.Session.Document.HasUnsavedChanges)
            {
                _env.AutoSave.Snapshot(_vm.Session.Document);
                _vm.StatusText = $"Auto-saved recovery snapshot at {DateTime.Now:T}";
            }
        }
        catch (Exception)
        {
            // recovery snapshots are best-effort
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_vm.ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        _autoSaveTimer.Stop();
        _env.AutoSave.ClearFor(_vm.Session.Project.Metadata.Id);
        _env.SaveSettings();
        base.OnClosing(e);
    }
}
