using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LabelMaker.App.Services;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Model;
using LabelMaker.Data;
using Microsoft.Win32;

namespace LabelMaker.App.Views;

/// <summary>
/// Connect to a database and choose the records: the kind of database, how to reach
/// and sign in to it, a table or a SELECT query, and a preview of the first rows.
/// Everything that talks to the server runs in the background with a Stop button, so
/// an unreachable server never freezes the window.
/// </summary>
public partial class DataConnectionWindow : Window
{
    private const int PreviewRows = 50;

    private readonly DataConnectionModel _connection;
    private CancellationTokenSource? _busy;
    private bool _loading = true;

    public DataConnectionWindow(DataConnectionModel? existing, string? knownPassword)
    {
        InitializeComponent();
        _connection = existing?.Clone() ?? new DataConnectionModel { Kind = DataSourceKind.Sqlite, Name = string.Empty };

        KindList.ItemsSource = Kinds;
        LoadDsns();
        Fill(knownPassword);
        _loading = false;
        UpdateForm();
        Closed += (_, _) => _busy?.Cancel();
    }

    /// <summary>The connection and password chosen, when the window closed with "Use these records".</summary>
    public DataConnectionChoice? Result { get; private set; }

    private sealed record KindItem(DataSourceKind Kind, string Title, string Hint)
    {
        public override string ToString() => Title;    // screen readers and type-to-search
    }

    private static readonly KindItem[] Kinds =
    {
        new(DataSourceKind.Sqlite, "SQLite", "A .db / .sqlite file"),
        new(DataSourceKind.SqlServer, "SQL Server", "Also Azure SQL and SQL Express"),
        new(DataSourceKind.MySql, "MySQL / MariaDB", "Server over the network"),
        new(DataSourceKind.PostgreSql, "PostgreSQL", "Server over the network"),
        new(DataSourceKind.Access, "Microsoft Access", ".accdb or .mdb file"),
        new(DataSourceKind.Odbc, "ODBC", "Any driver set up in Windows"),
    };

    private DataSourceKind Kind => (KindList.SelectedItem as KindItem)?.Kind ?? DataSourceKind.Sqlite;

    // ---- form <-> model -------------------------------------------------------

    private void Fill(string? knownPassword)
    {
        KindList.SelectedItem = Kinds.FirstOrDefault(k => k.Kind == _connection.Kind) ?? Kinds[0];
        NameBox.Text = _connection.Name == "Data" ? string.Empty : _connection.Name;
        FileBox.Text = _connection.SourcePath ?? string.Empty;
        ServerBox.Text = _connection.Server ?? string.Empty;
        PortBox.Text = _connection.Port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        DatabaseBox.Text = _connection.Database ?? string.Empty;
        DsnBox.Text = _connection.OdbcDsn ?? string.Empty;
        AuthBox.SelectedIndex = _connection.IntegratedSecurity ? 1 : 0;
        UserBox.Text = _connection.UserName ?? string.Empty;
        PasswordBox.Password = knownPassword ?? string.Empty;
        RememberBox.IsChecked = _connection.ProtectedPassword is not null;

        string options = _connection.ConnectionOptions ?? string.Empty;
        TrustCertBox.IsChecked = options.Contains("TrustServerCertificate=True", StringComparison.OrdinalIgnoreCase);
        OptionsBox.Text = RemoveTrustOption(options);
        TimeoutBox.Text = _connection.CommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture);

        TableBox.Text = _connection.TableName ?? string.Empty;
        QueryBox.Text = _connection.Query ?? string.Empty;
        QueryMode.IsChecked = !string.IsNullOrWhiteSpace(_connection.Query);
        TableMode.IsChecked = QueryMode.IsChecked != true;
        LimitBox.Text = _connection.MaxRows.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The connection as the form describes it now.</summary>
    private DataConnectionModel Read()
    {
        DataConnectionModel c = _connection.Clone();
        c.Kind = Kind;
        c.SourcePath = Kind.IsFileDatabase() ? NullIfBlank(FileBox.Text) : null;
        c.Server = Kind.IsServerDatabase() ? NullIfBlank(ServerBox.Text) : null;
        c.Port = Kind.IsServerDatabase() && int.TryParse(PortBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port > 0
            ? port
            : null;
        c.Database = Kind.IsServerDatabase() ? NullIfBlank(DatabaseBox.Text) : null;
        c.OdbcDsn = Kind == DataSourceKind.Odbc ? NullIfBlank(DsnBox.Text) : null;
        c.IntegratedSecurity = Kind == DataSourceKind.SqlServer && AuthBox.SelectedIndex == 1;
        c.UserName = UsesLogin ? NullIfBlank(UserBox.Text) : null;

        string options = OptionsBox.Text.Trim().TrimEnd(';');
        if (Kind == DataSourceKind.SqlServer && TrustCertBox.IsChecked == true)
        {
            options = options.Length == 0 ? "TrustServerCertificate=True" : options + ";TrustServerCertificate=True";
        }

        c.ConnectionOptions = NullIfBlank(options);
        c.CommandTimeoutSeconds = int.TryParse(TimeoutBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int timeout)
            ? Math.Clamp(timeout, 5, 3600)
            : 30;

        bool query = QueryMode.IsChecked == true;
        c.Query = query ? NullIfBlank(QueryBox.Text) : null;
        c.TableName = query ? null : NullIfBlank(TableBox.Text);
        c.MaxRows = int.TryParse(LimitBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) ? Math.Max(0, limit) : 0;

        c.Name = NullIfBlank(NameBox.Text) ?? DefaultName(c);
        return c;
    }

    private bool UsesLogin => Kind switch
    {
        DataSourceKind.SqlServer => AuthBox.SelectedIndex == 0,
        DataSourceKind.MySql or DataSourceKind.PostgreSql or DataSourceKind.Odbc => true,
        _ => false,
    };

    private static string DefaultName(DataConnectionModel c)
    {
        string where = c.Kind.IsFileDatabase()
            ? System.IO.Path.GetFileNameWithoutExtension(c.SourcePath ?? "database")
            : c.Database ?? c.OdbcDsn ?? c.Server ?? c.Kind.DisplayName();
        string what = c.TableName ?? (c.Query is not null ? "query" : null) ?? string.Empty;
        return what.Length > 0 ? $"{where} · {what}" : where;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string RemoveTrustOption(string options) =>
        string.Join(';', options.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.StartsWith("TrustServerCertificate", StringComparison.OrdinalIgnoreCase)));

    private void LoadDsns()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(@"Software\ODBC\ODBC.INI\ODBC Data Sources");
                foreach (string name in key?.GetValueNames() ?? Array.Empty<string>())
                {
                    names.Add(name);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            {
                // A locked-down registry just means no list; the name can still be typed.
            }
        }

        DsnBox.ItemsSource = names.ToList();
    }

    // ---- showing the right fields -----------------------------------------------

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            TableBox.ItemsSource = null;
            ClearPreview();
        }

        UpdateForm();
    }

    private void OnAuthChanged(object sender, SelectionChangedEventArgs e) => UpdateForm();

    private void OnModeChanged(object sender, RoutedEventArgs e) => UpdateForm();

    private void OnFormChanged(object sender, TextChangedEventArgs e) => SetStatus(null);

    private void OnFormChanged(object sender, SelectionChangedEventArgs e) => SetStatus(null);

    private void UpdateForm()
    {
        if (_loading || !IsInitialized)
        {
            return;
        }

        DataSourceKind kind = Kind;
        FileRow.Visibility = Show(kind.IsFileDatabase());
        ServerRows.Visibility = Show(kind.IsServerDatabase());
        OdbcRows.Visibility = Show(kind == DataSourceKind.Odbc);
        AuthRow.Visibility = Show(kind == DataSourceKind.SqlServer);
        LoginRows.Visibility = Show(UsesLogin);
        // SQLite files have no password (the bundled engine does not encrypt); Access files can.
        bool password = UsesLogin || kind == DataSourceKind.Access;
        PasswordRow.Visibility = Show(password);
        RememberBox.Visibility = Show(password);
        TrustCertBox.Visibility = Show(kind == DataSourceKind.SqlServer);

        if (kind.IsServerDatabase() && string.IsNullOrEmpty(PortBox.Text))
        {
            PortBox.Tag = DatabaseProviders.For(kind).DefaultPort?.ToString(CultureInfo.InvariantCulture);
        }

        TableRow.IsEnabled = TableMode.IsChecked == true;
        QueryBox.IsEnabled = QueryMode.IsChecked == true;
        ValidateQuery();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnQueryChanged(object sender, TextChangedEventArgs e) => ValidateQuery();

    private void ValidateQuery()
    {
        if (QueryMode.IsChecked != true || string.IsNullOrWhiteSpace(QueryBox.Text))
        {
            QueryProblem.Text = string.Empty;
            return;
        }

        QueryCheck check = SqlQueryGuard.Check(QueryBox.Text, DatabaseProviders.For(Kind).Dialect);
        QueryProblem.Text = check.IsAllowed ? string.Empty : check.Problem;
    }

    private void OnTableChosen(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && TableBox.SelectedItem is string table)
        {
            // An editable combo updates its text after SelectionChanged; set it now so the preview reads the new table.
            TableBox.Text = table;
            _ = PreviewAsync();
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Kind == DataSourceKind.Access ? "Choose an Access database" : "Choose a SQLite database",
            Filter = Kind == DataSourceKind.Access
                ? "Access databases|*.accdb;*.mdb|All files|*.*"
                : "SQLite databases|*.db;*.sqlite;*.sqlite3;*.db3|All files|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            FileBox.Text = dialog.FileName;
            TableBox.ItemsSource = null;
            _ = ListTablesAsync();
        }
    }

    // ---- talking to the database ---------------------------------------------------

    private async void OnTest(object sender, RoutedEventArgs e) => await RunAsync("Connecting…", async (c, token) =>
    {
        string password = PasswordBox.Password;
        string result = await Task.Run(() => DatabaseReader.TestAsync(c, password, token), token);
        SetStatus(result, ok: true);
    });

    private async void OnListTables(object sender, RoutedEventArgs e) => await ListTablesAsync();

    private Task ListTablesAsync() => RunAsync("Reading the list of tables…", async (c, token) =>
    {
        string password = PasswordBox.Password;
        IReadOnlyList<string> tables = await Task.Run(() => DatabaseReader.ListTablesAsync(c, password, token), token);
        string typed = TableBox.Text;
        TableBox.ItemsSource = tables;
        if (tables.Contains(typed))
        {
            TableBox.SelectedItem = typed;
        }
        else if (string.IsNullOrWhiteSpace(typed) && tables.Count > 0)
        {
            TableBox.IsDropDownOpen = true;
        }

        SetStatus(tables.Count == 0 ? "Connected, but there are no tables to read." : $"{tables.Count} table(s) and view(s)", ok: tables.Count > 0);
    });

    private async void OnPreview(object sender, RoutedEventArgs e) => await PreviewAsync();

    private Task PreviewAsync() => RunAsync("Reading the first rows…", async (c, token) =>
    {
        string password = PasswordBox.Password;
        RawTable raw = await Task.Run(() => DatabaseReader.ReadAsync(c, password, PreviewRows, null, token), token);
        ShowPreview(raw);
        int shown = Math.Max(0, raw.RowCount - 1);
        SetStatus(shown == 0 ? "The query ran but returned no rows." : $"Preview ready · {raw.Rows[0].Count} column(s)", ok: shown > 0);
    });

    private async void OnUse(object sender, RoutedEventArgs e)
    {
        DataConnectionModel c = Read();

        // Check what can be checked here, so mistakes are fixed in this window, not after it closes.
        await RunAsync("Checking…", async (_, token) =>
        {
            DatabaseReader.BuildSql(c, 1);
            string password = PasswordBox.Password;
            await Task.Run(() => DatabaseReader.TestAsync(c, password, token), token);

            c.ProtectedPassword = RememberBox.IsChecked == true && password.Length > 0
                ? CredentialProtector.Protect(password)
                : null;
            Result = new DataConnectionChoice(c, password);
            DialogResult = true;
        });
    }

    private void OnStop(object sender, RoutedEventArgs e) => _busy?.Cancel();

    /// <summary>Runs one background step with the form locked, a Stop button and friendly errors.</summary>
    private async Task RunAsync(string message, Func<DataConnectionModel, CancellationToken, Task> work)
    {
        if (_busy is not null)
        {
            return;
        }

        DataConnectionModel c = Read();
        using var cts = new CancellationTokenSource();
        _busy = cts;
        SetBusy(true, message);
        try
        {
            await work(c, cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Stopped.");
        }
        catch (DataSourceException ex)
        {
            SetStatus(FirstLine(ex.Message), ok: false, detail: ex.Message);
        }
        catch (NotSupportedException ex)
        {
            SetStatus(ex.Message, ok: false);
        }
        finally
        {
            _busy = null;
            if (IsLoaded)
            {
                SetBusy(false, null);
            }
        }
    }

    private void SetBusy(bool busy, string? message)
    {
        StopButton.Visibility = Show(busy);
        foreach (UIElement element in new UIElement[] { TestButton, PreviewButton, UseButton, KindList })
        {
            element.IsEnabled = !busy;
        }

        Cursor = busy ? System.Windows.Input.Cursors.AppStarting : null;
        if (busy)
        {
            SetStatus(message);
        }
    }

    private void SetStatus(string? message, bool? ok = null, string? detail = null)
    {
        if (StatusText is null)
        {
            return;
        }

        StatusText.Text = message ?? string.Empty;
        StatusText.ToolTip = detail;
        StatusDot.Fill = ok switch
        {
            true => new SolidColorBrush(Color.FromRgb(0x1F, 0x9D, 0x55)),
            false => (Brush)FindResource("Br.Danger"),
            _ => (Brush)FindResource("Br.TextMuted"),
        };

        if (ok == false && detail is not null && detail.Contains('\n'))
        {
            MessageBox.Show(this, detail, "Data connection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string FirstLine(string text)
    {
        int newline = text.IndexOf('\n');
        return (newline < 0 ? text : text[..newline]).TrimEnd(':', ' ', '\r');
    }

    // ---- preview ---------------------------------------------------------------------

    private void ClearPreview()
    {
        PreviewGrid.Columns.Clear();
        PreviewGrid.ItemsSource = null;
        PreviewEmpty.Visibility = Visibility.Visible;
        PreviewCaption.Text = string.Empty;
    }

    private void ShowPreview(RawTable raw)
    {
        PreviewGrid.Columns.Clear();
        if (raw.RowCount == 0)
        {
            ClearPreview();
            return;
        }

        IReadOnlyList<string> header = raw.Rows[0];
        for (int i = 0; i < header.Count; i++)
        {
            // Bind by position: column names may contain dots or brackets that break binding paths.
            PreviewGrid.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = header[i] },
                Binding = new Binding($"[{i}]"),
                MaxWidth = 320,
            });
        }

        PreviewGrid.ItemsSource = raw.Rows.Skip(1).ToList();
        PreviewEmpty.Visibility = raw.RowCount > 1 ? Visibility.Collapsed : Visibility.Visible;
        PreviewEmpty.Text = raw.RowCount > 1 ? string.Empty : "No rows.";
        int rows = raw.RowCount - 1;
        PreviewCaption.Text = rows >= PreviewRows ? $"first {PreviewRows} rows" : $"{rows} row(s)";
    }
}
