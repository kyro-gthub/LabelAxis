using System.IO;
using LabelMaker.Core.Formtec;
using LabelMaker.Core.Printing;
using LabelMaker.Core.Serialization;
using LabelMaker.Core.Settings;

namespace LabelMaker.App.Services;

/// <summary>Process-wide services: paths, settings, template catalogue, calibration, recovery.</summary>
public sealed class AppEnvironment
{
    public AppEnvironment()
    {
        Paths = new AppPaths();
        Paths.EnsureCreated();

        Settings = AppSettings.Load(Paths.SettingsFilePath);
        Templates = TemplateStore.CreateWithBuiltIns();
        Templates.LoadUserDirectory(Paths.UserTemplatesDirectory);
        Calibration = CalibrationStore.Load(Paths.CalibrationFilePath);
        AutoSave = new AutoSaveService(Paths.RecoveryDirectory)
        {
            Interval = TimeSpan.FromSeconds(Math.Max(30, Settings.AutoSaveSeconds)),
        };
    }

    public AppPaths Paths { get; }

    public AppSettings Settings { get; }

    public TemplateStore Templates { get; }

    public CalibrationStore Calibration { get; }

    public AutoSaveService AutoSave { get; }

    public void SaveSettings()
    {
        try
        {
            Settings.Save(Paths.SettingsFilePath);
        }
        catch (IOException)
        {
            // non-fatal
        }
    }
}
