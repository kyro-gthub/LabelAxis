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

    public MainWindow(AppEnvironment env)
    {
        _env = env;
        InitializeComponent();

        var shell = new DialogService(this, env);
        _vm = new MainViewModel(env, shell);
        shell.Bind(_vm);
        DataContext = _vm;

        _canvas = new DesignerCanvas(_vm.SheetRenderer);
        _canvas.ContextMenu = BuildCanvasContextMenu();
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

        menu.Items.Add(Item("_Select all", new RelayCommand(() =>
            _vm.Session.SetSelection(_vm.Session.ActiveLabel.Objects))));

        return menu;
    }

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
