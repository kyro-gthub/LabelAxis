using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using LabelMaker.Core.Model;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace LabelMaker.Data;

/// <summary>What LabelAxis needs from one kind of database: how to connect, quote, limit and list tables.</summary>
public interface IDatabaseProvider
{
    DataSourceKind Kind { get; }

    SqlDialect Dialect { get; }

    /// <summary>Default TCP port, for the connection form (null for file and ODBC sources).</summary>
    int? DefaultPort { get; }

    DbConnection CreateConnection(DataConnectionModel connection, string? password);

    /// <summary>Reads a whole table or view, stopping at <paramref name="maxRows"/> when it is above zero.</summary>
    string SelectTable(string table, int maxRows);

    Task<IReadOnlyList<string>> ListTablesAsync(DbConnection connection, CancellationToken cancellation);

    /// <summary>Makes the session read-only before the read transaction starts (null when the database has no such switch).</summary>
    string? ReadOnlySessionSql => null;

    /// <summary>Makes the transaction itself read-only, run just after it begins.</summary>
    string? ReadOnlyTransactionSql => null;
}

public static class DatabaseProviders
{
    private static readonly Dictionary<DataSourceKind, IDatabaseProvider> ByKind = new IDatabaseProvider[]
    {
        new SqliteProvider(),
        new SqlServerProvider(),
        new MySqlProvider(),
        new PostgreSqlProvider(),
        new OdbcProvider(),
        new AccessProvider(),
    }.ToDictionary(p => p.Kind);

    public static IDatabaseProvider For(DataSourceKind kind) =>
        ByKind.TryGetValue(kind, out IDatabaseProvider? provider)
            ? provider
            : throw new NotSupportedException($"{kind.DisplayName()} is not a database source.");

    /// <summary>Quotes each part of a possibly schema-qualified name ("dbo.Products").</summary>
    internal static string Quote(string name, char open, char close)
    {
        string[] parts = name.Split('.', StringSplitOptions.TrimEntries);
        return string.Join('.', parts.Select(part =>
        {
            string doubled = new(close, 2);
            string bare = part.Length >= 2 && part[0] == open && part[^1] == close
                ? part[1..^1].Replace(doubled, close.ToString(), StringComparison.Ordinal)
                : part;
            return open + bare.Replace(close.ToString(), new string(close, 2), StringComparison.Ordinal) + close;
        }));
    }

    /// <summary>Copies "Key=Value; Key2=Value2" options onto a provider's own connection-string builder.</summary>
    internal static void ApplyOptions(DbConnectionStringBuilder builder, string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
        {
            return;
        }

        var parsed = new DbConnectionStringBuilder { ConnectionString = options };
        foreach (string key in parsed.Keys)
        {
            try
            {
                builder[key] = parsed[key];
            }
            catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or FormatException)
            {
                throw new DataSourceException($"The connection option \"{key}\" is not understood by {builder.GetType().Name.Replace("ConnectionStringBuilder", string.Empty)}.", ex);
            }
        }
    }

    internal static async Task<IReadOnlyList<string>> QueryNamesAsync(DbConnection connection, string sql, CancellationToken cancellation)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        var names = new List<string>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0))
            {
                names.Add(reader.GetString(0));
            }
        }

        return names;
    }

    /// <summary>Tables and views from the ADO.NET schema collection, for drivers without an information schema.</summary>
    internal static IReadOnlyList<string> SchemaTables(DbConnection connection)
    {
        DataTable tables = connection.GetSchema("Tables");
        string? typeColumn = tables.Columns.Contains("TABLE_TYPE") ? "TABLE_TYPE" : null;
        return tables.Rows.Cast<DataRow>()
            .Where(r => typeColumn is null || r[typeColumn]?.ToString() is "TABLE" or "VIEW")
            .Select(r => r["TABLE_NAME"]?.ToString())
            .Where(n => !string.IsNullOrEmpty(n) && !n.StartsWith("MSys", StringComparison.OrdinalIgnoreCase))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

internal sealed class SqliteProvider : IDatabaseProvider
{
    public DataSourceKind Kind => DataSourceKind.Sqlite;

    public SqlDialect Dialect => SqlDialect.Standard;

    public int? DefaultPort => null;

    public DbConnection CreateConnection(DataConnectionModel c, string? password)
    {
        if (string.IsNullOrWhiteSpace(c.SourcePath))
        {
            throw new DataSourceException("Choose the SQLite database file.");
        }

        if (!File.Exists(c.SourcePath))
        {
            throw new DataSourceException($"The database file was not found:\n{c.SourcePath}");
        }

        // Read-only at the file level: nothing LabelAxis runs can change the database.
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = c.SourcePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        DatabaseProviders.ApplyOptions(builder, c.ConnectionOptions);
        builder.Mode = SqliteOpenMode.ReadOnly;
        return new SqliteConnection(builder.ConnectionString);
    }

    public string SelectTable(string table, int maxRows) =>
        $"SELECT * FROM {DatabaseProviders.Quote(table, '"', '"')}" + (maxRows > 0 ? $" LIMIT {maxRows}" : string.Empty);

    public Task<IReadOnlyList<string>> ListTablesAsync(DbConnection connection, CancellationToken cancellation) =>
        DatabaseProviders.QueryNamesAsync(connection,
            "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name COLLATE NOCASE",
            cancellation);
}

internal sealed class SqlServerProvider : IDatabaseProvider
{
    public DataSourceKind Kind => DataSourceKind.SqlServer;

    public SqlDialect Dialect => SqlDialect.Standard;

    public int? DefaultPort => 1433;

    public DbConnection CreateConnection(DataConnectionModel c, string? password)
    {
        if (string.IsNullOrWhiteSpace(c.Server))
        {
            throw new DataSourceException("Enter the server name, for example SERVER\\SQLEXPRESS or db.example.com.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = c.Port is { } port && port != DefaultPort ? $"{c.Server},{port}" : c.Server,
            InitialCatalog = c.Database ?? string.Empty,
            IntegratedSecurity = c.IntegratedSecurity,
            ConnectTimeout = 15,
            ApplicationName = "LabelAxis",
            ApplicationIntent = ApplicationIntent.ReadOnly,
            Pooling = false,
        };
        if (!c.IntegratedSecurity)
        {
            builder.UserID = c.UserName ?? string.Empty;
            builder.Password = password ?? string.Empty;
        }

        DatabaseProviders.ApplyOptions(builder, c.ConnectionOptions);
        return new SqlConnection(builder.ConnectionString);
    }

    public string SelectTable(string table, int maxRows) =>
        $"SELECT {(maxRows > 0 ? $"TOP ({maxRows}) " : string.Empty)}* FROM {DatabaseProviders.Quote(table, '[', ']')}";

    public Task<IReadOnlyList<string>> ListTablesAsync(DbConnection connection, CancellationToken cancellation) =>
        DatabaseProviders.QueryNamesAsync(connection,
            "SELECT TABLE_SCHEMA + '.' + TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_SCHEMA, TABLE_NAME",
            cancellation);
}

internal sealed class MySqlProvider : IDatabaseProvider
{
    public DataSourceKind Kind => DataSourceKind.MySql;

    public string? ReadOnlySessionSql => "SET SESSION TRANSACTION READ ONLY";

    public SqlDialect Dialect => SqlDialect.MySql;

    public int? DefaultPort => 3306;

    public DbConnection CreateConnection(DataConnectionModel c, string? password)
    {
        if (string.IsNullOrWhiteSpace(c.Server))
        {
            throw new DataSourceException("Enter the server name or address.");
        }

        var builder = new MySqlConnectionStringBuilder
        {
            Server = c.Server,
            Port = (uint)(c.Port ?? DefaultPort!.Value),
            Database = c.Database ?? string.Empty,
            UserID = c.UserName ?? string.Empty,
            Password = password ?? string.Empty,
            ConnectionTimeout = 15,
            Pooling = false,
            AllowUserVariables = false,
            AllowLoadLocalInfile = false,
        };
        DatabaseProviders.ApplyOptions(builder, c.ConnectionOptions);
        builder.AllowLoadLocalInfile = false;
        return new MySqlConnection(builder.ConnectionString);
    }

    public string SelectTable(string table, int maxRows) =>
        $"SELECT * FROM {DatabaseProviders.Quote(table, '`', '`')}" + (maxRows > 0 ? $" LIMIT {maxRows}" : string.Empty);

    public Task<IReadOnlyList<string>> ListTablesAsync(DbConnection connection, CancellationToken cancellation) =>
        DatabaseProviders.QueryNamesAsync(connection,
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME",
            cancellation);
}

internal sealed class PostgreSqlProvider : IDatabaseProvider
{
    public DataSourceKind Kind => DataSourceKind.PostgreSql;

    public string? ReadOnlyTransactionSql => "SET TRANSACTION READ ONLY";

    public SqlDialect Dialect => SqlDialect.PostgreSql;

    public int? DefaultPort => 5432;

    public DbConnection CreateConnection(DataConnectionModel c, string? password)
    {
        if (string.IsNullOrWhiteSpace(c.Server))
        {
            throw new DataSourceException("Enter the server name or address.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = c.Server,
            Port = c.Port ?? DefaultPort!.Value,
            Database = c.Database,
            Username = c.UserName,
            Password = password,
            Timeout = 15,
            ApplicationName = "LabelAxis",
            Pooling = false,
        };
        DatabaseProviders.ApplyOptions(builder, c.ConnectionOptions);
        return new NpgsqlConnection(builder.ConnectionString);
    }

    public string SelectTable(string table, int maxRows) =>
        $"SELECT * FROM {DatabaseProviders.Quote(table, '"', '"')}" + (maxRows > 0 ? $" LIMIT {maxRows}" : string.Empty);

    public Task<IReadOnlyList<string>> ListTablesAsync(DbConnection connection, CancellationToken cancellation) =>
        DatabaseProviders.QueryNamesAsync(connection,
            "SELECT table_schema || '.' || table_name FROM information_schema.tables " +
            "WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY table_schema, table_name",
            cancellation);
}

internal sealed class OdbcProvider : IDatabaseProvider
{
    public DataSourceKind Kind => DataSourceKind.Odbc;

    public SqlDialect Dialect => SqlDialect.Standard;

    public int? DefaultPort => null;

    public DbConnection CreateConnection(DataConnectionModel c, string? password)
    {
        var builder = new OdbcConnectionStringBuilder();
        if (!string.IsNullOrWhiteSpace(c.OdbcDsn))
        {
            builder.Dsn = c.OdbcDsn;
        }
        else if (string.IsNullOrWhiteSpace(c.ConnectionOptions))
        {
            throw new DataSourceException("Choose an ODBC data source (DSN) or type a connection string.");
        }

        if (!string.IsNullOrWhiteSpace(c.ConnectionOptions))
        {
            // ODBC drivers each have their own keys, so take the text as typed.
            builder.ConnectionString = string.IsNullOrEmpty(builder.ConnectionString)
                ? c.ConnectionOptions
                : builder.ConnectionString + ";" + c.ConnectionOptions;
        }

        if (!string.IsNullOrWhiteSpace(c.UserName))
        {
            builder["UID"] = c.UserName;
        }

        if (!string.IsNullOrEmpty(password))
        {
            builder["PWD"] = password;
        }

        return new OdbcConnection(builder.ConnectionString) { ConnectionTimeout = 15 };
    }

    // Plain ODBC has no portable LIMIT; the reader stops at the row limit instead.
    public string SelectTable(string table, int maxRows) => $"SELECT * FROM {DatabaseProviders.Quote(table, '"', '"')}";

    public Task<IReadOnlyList<string>> ListTablesAsync(DbConnection connection, CancellationToken cancellation) =>
        Task.Run(() => DatabaseProviders.SchemaTables(connection), cancellation);
}

internal sealed class AccessProvider : IDatabaseProvider
{
    public DataSourceKind Kind => DataSourceKind.Access;

    public SqlDialect Dialect => SqlDialect.Standard;

    public int? DefaultPort => null;

    public DbConnection CreateConnection(DataConnectionModel c, string? password)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DataSourceException("Access databases can only be read on Windows.");
        }

        if (string.IsNullOrWhiteSpace(c.SourcePath) || !File.Exists(c.SourcePath))
        {
            throw new DataSourceException(string.IsNullOrWhiteSpace(c.SourcePath)
                ? "Choose the Access database file (.accdb or .mdb)."
                : $"The database file was not found:\n{c.SourcePath}");
        }

        var builder = new OleDbConnectionStringBuilder
        {
            Provider = "Microsoft.ACE.OLEDB.12.0",
            DataSource = c.SourcePath,
        };
        builder["Mode"] = "Read";
        if (!string.IsNullOrEmpty(password))
        {
            builder["Jet OLEDB:Database Password"] = password;
        }

        DatabaseProviders.ApplyOptions(builder, c.ConnectionOptions);
        return new OleDbConnection(builder.ConnectionString);
    }

    public string SelectTable(string table, int maxRows) =>
        $"SELECT {(maxRows > 0 ? $"TOP {maxRows} " : string.Empty)}* FROM {DatabaseProviders.Quote(table, '[', ']')}";

    public Task<IReadOnlyList<string>> ListTablesAsync(DbConnection connection, CancellationToken cancellation) =>
        Task.Run(() => DatabaseProviders.SchemaTables(connection), cancellation);
}
