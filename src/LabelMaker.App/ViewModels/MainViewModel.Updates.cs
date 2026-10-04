using System.IO;
using System.Net.Http;
using System.Text.Json;
using LabelMaker.App.Localization;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Updates;

namespace LabelMaker.App.ViewModels;

/// <summary>Updates: the daily check, Help › Check for updates, and the bar that offers a new version.</summary>
public sealed partial class MainViewModel
{
    private AppUpdater? _updater;
    private UpdateFeed? _update;
    private Uri? _updateFeedUri;
    private bool _updateBarVisible;
    private string _updateBarText = string.Empty;
    private bool _checkingForUpdates;

    public bool UpdateBarVisible { get => _updateBarVisible; private set => SetField(ref _updateBarVisible, value); }

    public string UpdateBarText { get => _updateBarText; private set => SetField(ref _updateBarText, value); }

    public RelayCommand CheckForUpdatesCommand { get; private set; } = null!;

    public RelayCommand InstallUpdateCommand { get; private set; } = null!;

    public RelayCommand UpdateNotesCommand { get; private set; } = null!;

    public RelayCommand DismissUpdateCommand { get; private set; } = null!;

    public RelayCommand SkipUpdateCommand { get; private set; } = null!;

    private AppUpdater Updater => _updater ??= new AppUpdater(_env);

    private void BuildUpdateCommands()
    {
        CheckForUpdatesCommand = new RelayCommand(() => _ = CheckForUpdatesNowAsync(), () => !_checkingForUpdates);
        InstallUpdateCommand = new RelayCommand(InstallUpdate, () => _update is not null);
        UpdateNotesCommand = new RelayCommand(ShowUpdateNotes, () => _update is not null);
        DismissUpdateCommand = new RelayCommand(() => UpdateBarVisible = false);
        SkipUpdateCommand = new RelayCommand(() =>
        {
            if (_update is not null)
            {
                Updater.Skip(_update);
                StatusText = Localizer.F("LabelAxis {0} will not be offered again. Help › Check for updates still finds it.", _update.Version);
            }

            UpdateBarVisible = false;
        });
    }

    private static bool IsUpdateProblem(Exception ex) =>
        ex is IOException or HttpRequestException or FormatException or JsonException or UnauthorizedAccessException
            or InvalidDataException or TaskCanceledException or UriFormatException;

    /// <summary>The daily look for a new version, run quietly once LabelAxis is up; a failure waits for the next day.</summary>
    public async Task CheckForUpdatesInBackgroundAsync()
    {
        if (!Updater.IsCheckDue || _checkingForUpdates)
        {
            return;
        }

        try
        {
            _checkingForUpdates = true;
            (UpdateFeed? feed, Uri feedUri) = await Updater.CheckAsync(CancellationToken.None);
            if (feed is not null && !Updater.IsSkipped(feed))
            {
                Offer(feed, feedUri);
            }
        }
        catch (Exception ex) when (IsUpdateProblem(ex))
        {
            // No network, server down, or a bad file: nothing to show; the next check tries again.
        }
        finally
        {
            _checkingForUpdates = false;
        }
    }

    private async Task CheckForUpdatesNowAsync()
    {
        if (!Updater.HasAddress)
        {
            _shell.Info("No update address is set yet. Enter the address where LabelAxis updates are published in Settings › Updates.");
            return;
        }

        _checkingForUpdates = true;
        CheckForUpdatesCommand.RaiseCanExecuteChanged();
        StatusText = "Checking for updates…";
        try
        {
            (UpdateFeed? feed, Uri feedUri) = await Updater.CheckAsync(CancellationToken.None);
            if (feed is null)
            {
                StatusText = Localizer.F("LabelAxis {0} is the latest version.", AppInfo.Version);
                _shell.Info(StatusText);
            }
            else
            {
                // Asked for: offered even if this version was skipped before.
                Offer(feed, feedUri);
                StatusText = Localizer.F("LabelAxis {0} is available.", feed.Version);
            }
        }
        catch (Exception ex) when (IsUpdateProblem(ex))
        {
            StatusText = "Could not check for updates";
            _shell.Warn("Could not check for updates:\n\n" + ex.Message);
        }
        finally
        {
            _checkingForUpdates = false;
            CheckForUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    private void Offer(UpdateFeed feed, Uri feedUri)
    {
        _update = feed;
        _updateFeedUri = feedUri;
        UpdateBarText = Localizer.F("LabelAxis {0} is available (you have {1}).", feed.Version, AppInfo.Version);
        UpdateBarVisible = true;
        InstallUpdateCommand.RaiseCanExecuteChanged();
        UpdateNotesCommand.RaiseCanExecuteChanged();
    }

    private void ShowUpdateNotes()
    {
        if (_update is null)
        {
            return;
        }

        string notes = _update.NotesFor(Localizer.Current.Code);
        _shell.ShowTextReport(
            Localizer.F("What's new in LabelAxis {0}", _update.Version),
            notes.Length > 0 ? notes : Localizer.T("No notes were published with this version."));
    }

    private void InstallUpdate()
    {
        if (_update is not { } update || _updateFeedUri is not { } feedUri)
        {
            return;
        }

        UpdatePackage? package = Updater.InstallerFor(update);
        if (package is null)
        {
            OpenPortableDownload(update, feedUri);
            return;
        }

        string target = AppUpdater.DownloadPath(update, package);
        try
        {
            Uri source = UpdateSource.Resolve(feedUri, package);
            int kilobytes = (int)Math.Clamp(package.Size / 1024, 0, int.MaxValue);
            _shell.RunJob(Localizer.F("Downloading LabelAxis {0}", update.Version), "KB downloaded", kilobytes, async (progress, token) =>
            {
                await UpdateSource.DownloadAsync(source, package, target,
                    new Progress<long>(bytes => progress.Report((int)Math.Min(int.MaxValue, bytes / 1024))), token);
                return true;
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Update download cancelled";
            return;
        }
        catch (Exception ex) when (IsUpdateProblem(ex))
        {
            _shell.Warn("The update could not be downloaded:\n\n" + ex.Message);
            return;
        }

        if (_shell.Confirm(Localizer.F(
                "LabelAxis {0} is ready to install. LabelAxis closes (asking to save any changes), installs the update and starts again.",
                update.Version)))
        {
            _shell.QuitAndRun(target, AppUpdater.SilentInstallArguments(Localizer.Current.Code));
        }
    }

    // A portable copy is replaced by hand: open the new zip's address (or its folder).
    private void OpenPortableDownload(UpdateFeed update, Uri feedUri)
    {
        if (update.Portable is not { } portable)
        {
            _shell.Info("This copy of LabelAxis is portable, so it is not updated by itself. Get the new version where you got this one.");
            return;
        }

        try
        {
            Uri zip = UpdateSource.Resolve(feedUri, portable);
            _shell.OpenExternal(zip.IsFile ? Path.GetDirectoryName(zip.LocalPath) ?? zip.LocalPath : zip.AbsoluteUri);
            _shell.Info(Localizer.F(
                "This copy of LabelAxis is portable. Unzip the new version ({0}) over this folder, or next to it, to update it. Your settings in the Data folder are kept.",
                Path.GetFileName(zip.LocalPath)));
        }
        catch (FormatException ex)
        {
            _shell.Warn(ex.Message);
        }
    }
}
