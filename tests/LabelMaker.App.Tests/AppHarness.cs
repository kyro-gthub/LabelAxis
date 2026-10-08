using System.IO;
using LabelMaker.App.Services;
using LabelMaker.App.ViewModels;

namespace LabelMaker.App.Tests;

/// <summary>
/// The main view model as the main window uses it, with its settings, templates and recovery
/// files in a folder of its own that is deleted afterwards, and <see cref="FakeShell"/> in
/// place of the windows. Nothing touches the user's own settings, clipboard or screen.
/// </summary>
public sealed class AppHarness : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LabelAxis.AppTests." + Guid.NewGuid().ToString("N"));

    public AppHarness()
    {
        Environment = new AppEnvironment(_folder);
        Shell = new FakeShell();
        ViewModel = new MainViewModel(Environment, Shell);
    }

    public AppEnvironment Environment { get; }

    public FakeShell Shell { get; }

    public MainViewModel ViewModel { get; }

    public DocumentSession Session => ViewModel.Session;

    /// <summary>A path inside the harness's own folder, for files a test writes and reads.</summary>
    public string FileIn(string name) => Path.Combine(_folder, name);

    /// <summary>Runs <paramref name="test"/> against a fresh harness on a WPF-ready thread.</summary>
    public static void Run(Action<AppHarness> test) => Sta.Run(() =>
    {
        using var harness = new AppHarness();
        test(harness);
    });

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open; the temp folder is cleaned up by Windows in time.
        }
    }
}
