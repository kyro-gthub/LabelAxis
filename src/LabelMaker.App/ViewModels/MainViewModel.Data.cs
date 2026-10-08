using System.IO;
using LabelMaker.App.Mvvm;
using LabelMaker.App.Services;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Diagnostics;
using LabelMaker.Core.Model;
using LabelMaker.Data;

namespace LabelMaker.App.ViewModels;

/// <summary>Data sources: database connections, refreshing, editing records and printing a chosen set of them.</summary>
public sealed partial class MainViewModel
{
    // Passwords typed this session, so refreshing does not ask again. Never written anywhere.
    private readonly Dictionary<string, string> _sessionPasswords = new();

    /// <summary>Cells or rows of the current records were edited (the table object is unchanged).</summary>
    public event EventHandler? RecordsEdited;

    public RelayCommand ConnectDatabaseCommand { get; private set; } = null!;

    public RelayCommand RefreshDataCommand { get; private set; } = null!;

    public RelayCommand EditConnectionCommand { get; private set; } = null!;

    public bool CanRefreshData => _session.ActiveConnection is { } c &&
                                 (c.Kind.IsDatabase() || (c.Kind is DataSourceKind.Csv or DataSourceKind.Xlsx && c.SourcePath is not null));

    private void BuildDataCommands()
    {
        ConnectDatabaseCommand = new RelayCommand(() => ConnectDatabase(null));
        EditConnectionCommand = new RelayCommand(
            () => ConnectDatabase(_session.ActiveConnection),
            () => _session.ActiveConnection is { } c && c.Kind.IsDatabase());
        RefreshDataCommand = new RelayCommand(() => RefreshData(), () => CanRefreshData);
    }

    // ---- connect ------------------------------------------------------------

    private void ConnectDatabase(DataConnectionModel? existing)
    {
        string? known = existing is null ? null : KnownPassword(existing);
        if (_shell.ShowDataConnection(existing, known) is not { } choice)
        {
            return;
        }

        if (!ConfirmReplacingEditedRecords())
        {
            return;
        }

        LoadFromDatabase(choice.Connection, choice.Password);
    }

    /// <summary>Reads a database connection with a progress window and makes its rows the project's records.</summary>
    public bool LoadFromDatabase(DataConnectionModel connection, string? password)
    {
        try
        {
            DataSet data = _shell.RunJob($"Reading {connection.Kind.DisplayName()}", "records read", 0,
                (progress, token) => Task.Run(() => DatabaseReader.LoadDataSetAsync(connection, password, progress, token), token));

            if (!string.IsNullOrEmpty(password))
            {
                _sessionPasswords[connection.Id] = password;
            }

            connection.LastRefreshedUtc = DateTimeOffset.UtcNow;
            UseRecords(data, connection);
            StatusText = $"Read {data.RowCount:N0} record(s) from {connection.Describe()}";
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Reading the data was cancelled";
        }
        catch (DataSourceException ex)
        {
            _shell.Warn(ex.Message, "Data connection");
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn("The data could not be read:\n\n" + ex.Message, "Data connection");
        }

        return false;
    }

    /// <summary>Makes <paramref name="data"/> the project's records and forgets choices that referred to the old rows.</summary>
    private void UseRecords(DataSet data, DataConnectionModel connection)
    {
        PrintSettings print = _session.Project.PrintSettings;
        bool hadChosenRecords = print.SelectedRecords.Count > 0 || print.FirstRecord is not null || print.LastRecord is not null;

        // Row numbers chosen for printing pointed at the previous rows; keeping them would print the wrong labels.
        print.SelectedRecords.Clear();
        print.FirstRecord = null;
        print.LastRecord = null;

        _session.SetData(data, connection);
        _activeTab.RecordsEditedSinceLoad = false;
        OnPropertyChanged(nameof(CanRefreshData));
        RaiseAllCanExecute();

        if (hadChosenRecords)
        {
            StatusText = "The records changed, so the chosen records for printing were reset to all records.";
        }
    }

    // ---- refresh ------------------------------------------------------------

    /// <summary>Reads the current source again: the database, or the file the records were imported from.</summary>
    public bool RefreshData()
    {
        if (_session.ActiveConnection is not { } connection)
        {
            _shell.Info("These records are not linked to a file or database. Import a file or connect to a database first.");
            return false;
        }

        if (!ConfirmReplacingEditedRecords())
        {
            return false;
        }

        if (connection.Kind.IsDatabase())
        {
            if (ResolvePassword(connection) is not { } password)
            {
                return false;
            }

            return LoadFromDatabase(connection, password);
        }

        if (connection.SourcePath is not { } path || !File.Exists(path))
        {
            _shell.Warn($"The file these records came from can no longer be found:\n\n{connection.SourcePath}\n\nImport it again from its new location.");
            return false;
        }

        try
        {
            DataSet data = _shell.RunJob($"Reading {Path.GetFileName(path)}", "records", 0,
                (_, token) => Task.Run(() =>
                {
                    var service = new DataImportService();
                    RawTable raw = service.ReadRaw(path, new TabularReadOptions
                    {
                        Delimiter = connection.Delimiter,
                        EncodingName = connection.Encoding,
                        WorksheetName = connection.WorksheetName,
                    });
                    token.ThrowIfCancellationRequested();
                    return service.BuildDataSet(raw, connection);
                }, token));

            connection.LastRefreshedUtc = DateTimeOffset.UtcNow;
            UseRecords(data, connection);
            StatusText = $"Refreshed {data.RowCount:N0} record(s) from {Path.GetFileName(path)}";
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Refresh cancelled";
        }
        catch (Exception ex) when (Faults.IsReportable(ex))
        {
            _shell.Warn("The file could not be read:\n\n" + ex.Message);
        }

        return false;
    }

    private bool ConfirmReplacingEditedRecords() =>
        !_activeTab.RecordsEditedSinceLoad ||
        _shell.Confirm("You have edited these records in LabelAxis. Reading the source again replaces them with its current rows, and those edits are lost.\n\nContinue?");

    /// <summary>After a project opens with saved records, say where they came from and whether they may be out of date.</summary>
    private void DescribeRestoredData(DataConnectionModel? connection)
    {
        if (connection is null)
        {
            return;
        }

        string when = connection.LastRefreshedUtc is { } read ? $" as of {read.ToLocalTime():d MMM, HH:mm}" : string.Empty;
        if (connection.Kind.IsDatabase())
        {
            StatusText = $"Showing saved records{when}. Data > Refresh reads {connection.Kind.DisplayName()} again.";
        }
        else if (connection.SourcePath is { } path && File.Exists(path) && connection.LastRefreshedUtc is { } last &&
                 File.GetLastWriteTimeUtc(path) > last.UtcDateTime)
        {
            StatusText = $"{Path.GetFileName(path)} has changed since these records were read. Data > Refresh to update.";
        }
    }

    // ---- passwords ------------------------------------------------------------

    private static bool NeedsPassword(DataConnectionModel c) =>
        (c.Kind.IsServerDatabase() && !c.IntegratedSecurity && !string.IsNullOrEmpty(c.UserName)) ||
        (c.Kind == DataSourceKind.Odbc && !string.IsNullOrEmpty(c.UserName));

    private string? KnownPassword(DataConnectionModel c) =>
        _sessionPasswords.TryGetValue(c.Id, out string? typed) ? typed : CredentialProtector.TryUnprotect(c.ProtectedPassword);

    /// <summary>The password to connect with: remembered, typed earlier this session, or asked for now. Null if the user cancels.</summary>
    private string? ResolvePassword(DataConnectionModel c)
    {
        if (KnownPassword(c) is { } known)
        {
            return known;
        }

        if (!NeedsPassword(c))
        {
            return string.Empty;
        }

        string? typed = _shell.PromptPassword("Database password",
            $"Password for {c.UserName} on {c.Server ?? c.OdbcDsn ?? c.Kind.DisplayName()}");
        if (typed is not null)
        {
            _sessionPasswords[c.Id] = typed;
        }

        return typed;
    }

    // ---- editing and choosing records -------------------------------------------

    public void EditRecordCell(DataRecord record, int column, string value)
    {
        if (_session.Data is not { } data || record.RawValues[column] == value)
        {
            return;
        }

        _session.Execute(new EditRecordCellCommand(_session, data, record, column, value));
        _activeTab.RecordsEditedSinceLoad = true;
    }

    public DataRecord? AddRecord(int atIndex)
    {
        if (_session.Data is not { Columns.Count: > 0 } data)
        {
            return null;
        }

        DataRecord record = data.CreateRecord();
        _session.Execute(new InsertRecordsCommand(_session, data, atIndex, new[] { record }));
        _activeTab.RecordsEditedSinceLoad = true;
        OnPropertyChanged(nameof(HasData));
        return record;
    }

    public void DeleteRecords(IReadOnlyList<DataRecord> records)
    {
        if (_session.Data is not { } data || records.Count == 0)
        {
            return;
        }

        _session.Execute(new RemoveRecordsCommand(_session, data, records));
        _activeTab.RecordsEditedSinceLoad = true;

        // Row numbers chosen for printing may now point at other rows.
        _session.Project.PrintSettings.SelectedRecords.Clear();
        StatusText = records.Count == 1 ? "Deleted 1 record" : $"Deleted {records.Count:N0} records";
    }

    /// <summary>Opens the print dialog set to print one label for each of these records (0-based row numbers).</summary>
    public void PrintRecords(IReadOnlyList<int> recordIndices)
    {
        if (recordIndices.Count == 0)
        {
            return;
        }

        PrintSettings settings = _session.Project.PrintSettings;
        settings.Content = PrintContent.VariableData;
        settings.SelectedRecords = recordIndices.Distinct().Order().ToList();
        settings.FirstRecord = null;
        settings.LastRecord = null;
        settings.FirstSheet = null;
        settings.LastSheet = null;
        _activeTab.HasConfiguredPrintSettings = true;
        Print();
    }
}
