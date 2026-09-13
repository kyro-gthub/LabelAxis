namespace LabelMaker.Core.Model;

public enum DataSourceKind
{
    Manual = 0,
    Csv = 1,
    Xlsx = 2,
    Clipboard = 3,

    /// <summary>SQLite database file.</summary>
    Sqlite = 4,

    /// <summary>Microsoft SQL Server / Azure SQL.</summary>
    SqlServer = 5,

    /// <summary>MySQL or MariaDB.</summary>
    MySql = 6,

    PostgreSql = 7,

    /// <summary>Any ODBC data source (DSN or connection string).</summary>
    Odbc = 8,

    /// <summary>Microsoft Access .accdb / .mdb file (needs the Access Database Engine).</summary>
    Access = 9,
}

public static class DataSourceKindExtensions
{
    public static bool IsDatabase(this DataSourceKind kind) => kind >= DataSourceKind.Sqlite;

    /// <summary>Databases that live in a single file chosen with a file picker.</summary>
    public static bool IsFileDatabase(this DataSourceKind kind) => kind is DataSourceKind.Sqlite or DataSourceKind.Access;

    /// <summary>Databases reached over the network with a server name and a login.</summary>
    public static bool IsServerDatabase(this DataSourceKind kind) =>
        kind is DataSourceKind.SqlServer or DataSourceKind.MySql or DataSourceKind.PostgreSql;

    public static string DisplayName(this DataSourceKind kind) => kind switch
    {
        DataSourceKind.Csv => "CSV / text file",
        DataSourceKind.Xlsx => "Excel workbook",
        DataSourceKind.Clipboard => "Pasted data",
        DataSourceKind.Sqlite => "SQLite",
        DataSourceKind.SqlServer => "SQL Server",
        DataSourceKind.MySql => "MySQL / MariaDB",
        DataSourceKind.PostgreSql => "PostgreSQL",
        DataSourceKind.Odbc => "ODBC",
        DataSourceKind.Access => "Microsoft Access",
        _ => "Typed in",
    };
}

/// <summary>
/// How a project reconnects to its data on load. The imported rows are cached in
/// the project archive (<c>data/&lt;id&gt;.csv</c>) so a project opens offline
/// even if the original file has moved; <see cref="ReloadOnOpen"/> re-reads the
/// original when it is still reachable.
/// </summary>
public sealed class DataConnectionModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Data";

    public DataSourceKind Kind { get; set; } = DataSourceKind.Manual;

    /// <summary>Absolute or project-relative path to the original CSV/XLSX (null for Manual/Clipboard).</summary>
    public string? SourcePath { get; set; }

    /// <summary>Worksheet name for XLSX sources.</summary>
    public string? WorksheetName { get; set; }

    /// <summary>0-based index of the header row within the sheet.</summary>
    public int HeaderRowIndex { get; set; }

    /// <summary>Delimiter override for CSV/TSV (null = auto-detect).</summary>
    public string? Delimiter { get; set; }

    /// <summary>Text encoding name for CSV (null = detect / UTF-8).</summary>
    public string? Encoding { get; set; }

    public bool ReloadOnOpen { get; set; } = true;

    /// <summary>0-based row indices excluded during import (spec §35).</summary>
    public List<int> ExcludedRows { get; set; } = new();

    /// <summary>Optional rename map: source column name → display field name.</summary>
    public Dictionary<string, string> ColumnRenames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Simple "column = value" / "column contains value" filters applied on load.</summary>
    public List<DataFilter> Filters { get; set; } = new();

    /// <summary>Column to sort by after load (null = source order).</summary>
    public string? SortColumn { get; set; }

    public bool SortDescending { get; set; }

    // ---- databases -----------------------------------------------------------

    /// <summary>Host name (and instance, for SQL Server) of a server database.</summary>
    public string? Server { get; set; }

    /// <summary>TCP port; null uses the database's default.</summary>
    public int? Port { get; set; }

    /// <summary>Database (catalog) name on the server.</summary>
    public string? Database { get; set; }

    public string? UserName { get; set; }

    /// <summary>Sign in as the current Windows user (SQL Server) instead of a user name and password.</summary>
    public bool IntegratedSecurity { get; set; }

    /// <summary>
    /// The password, encrypted for the current Windows user, when the user chose to
    /// remember it. Never stored in plain text; null means ask each time.
    /// </summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>ODBC data source name, when connecting through a DSN.</summary>
    public string? OdbcDsn { get; set; }

    /// <summary>Extra connection-string options (for example <c>Encrypt=false</c>), appended as typed.</summary>
    public string? ConnectionOptions { get; set; }

    /// <summary>Table or view to read. Ignored when <see cref="Query"/> is set.</summary>
    public string? TableName { get; set; }

    /// <summary>A read-only SELECT statement; takes precedence over <see cref="TableName"/>.</summary>
    public string? Query { get; set; }

    /// <summary>Stop after this many rows (0 = no limit).</summary>
    public int MaxRows { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>When the records were last read from the source.</summary>
    public DateTimeOffset? LastRefreshedUtc { get; set; }

    /// <summary>One line naming where the records come from, for panels and the print log.</summary>
    public string Describe()
    {
        if (Kind.IsFileDatabase() || Kind is DataSourceKind.Csv or DataSourceKind.Xlsx)
        {
            string file = SourcePath is null ? "no file" : Path.GetFileName(SourcePath);
            string what = Kind.IsDatabase() ? (Query is { Length: > 0 } ? " · query" : TableName is { Length: > 0 } t ? " · " + t : string.Empty) : string.Empty;
            return $"{Kind.DisplayName()} · {file}{what}";
        }

        if (Kind.IsDatabase())
        {
            string where = Kind == DataSourceKind.Odbc
                ? OdbcDsn ?? "connection string"
                : $"{Server}{(Port is { } p ? ":" + p : string.Empty)}/{Database}";
            string what = Query is { Length: > 0 } ? "query" : TableName ?? "no table";
            return $"{Kind.DisplayName()} · {where} · {what}";
        }

        return Kind is DataSourceKind.Manual or DataSourceKind.Clipboard && !string.IsNullOrWhiteSpace(Name) && Name != "Data"
            ? Name
            : Kind.DisplayName();
    }

    public DataConnectionModel Clone() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        SourcePath = SourcePath,
        WorksheetName = WorksheetName,
        HeaderRowIndex = HeaderRowIndex,
        Delimiter = Delimiter,
        Encoding = Encoding,
        ReloadOnOpen = ReloadOnOpen,
        ExcludedRows = new List<int>(ExcludedRows),
        ColumnRenames = new Dictionary<string, string>(ColumnRenames, StringComparer.OrdinalIgnoreCase),
        Filters = Filters.Select(f => f.Clone()).ToList(),
        SortColumn = SortColumn,
        SortDescending = SortDescending,
        Server = Server,
        Port = Port,
        Database = Database,
        UserName = UserName,
        IntegratedSecurity = IntegratedSecurity,
        ProtectedPassword = ProtectedPassword,
        OdbcDsn = OdbcDsn,
        ConnectionOptions = ConnectionOptions,
        TableName = TableName,
        Query = Query,
        MaxRows = MaxRows,
        CommandTimeoutSeconds = CommandTimeoutSeconds,
        LastRefreshedUtc = LastRefreshedUtc,
    };
}

public enum DataFilterOperator
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    StartsWith = 3,
    GreaterThan = 4,
    LessThan = 5,
    IsEmpty = 6,
    IsNotEmpty = 7,
}

public sealed class DataFilter
{
    public string Column { get; set; } = string.Empty;
    public DataFilterOperator Operator { get; set; } = DataFilterOperator.Equals;
    public string Value { get; set; } = string.Empty;

    public DataFilter Clone() => (DataFilter)MemberwiseClone();
}
