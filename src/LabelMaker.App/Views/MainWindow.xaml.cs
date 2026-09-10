using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using LabelMaker.App.Controls;
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
        CanvasHost.Child = _canvas;
        _vm.AttachCanvas(_canvas);

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
