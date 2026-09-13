using System.Data;
using System.Data.Common;
using System.Globalization;
using LabelMaker.Core.Data;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Model;
using DataSet = LabelMaker.Core.Data.DataSet;

namespace LabelMaker.Data;

/// <summary>A data source problem worded for the person printing, not for a developer.</summary>
public sealed class DataSourceException : Exception
{
    public DataSourceException(string message)
        : base(message)
    {
    }

    public DataSourceException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Reads rows from a database connection into the same raw grid a CSV or Excel file
/// produces, so renames, filters, sorting and printing work the same for every source.
/// Always read-only: typed queries are checked first, and every read runs in a
/// transaction that is rolled back.
/// </summary>
public static class DatabaseReader
{
    private const int ProgressEvery = 500;

    /// <summary>Opens a connection and closes it again — "Test connection".</summary>
    public static async Task<string> TestAsync(DataConnectionModel connection, string? password, CancellationToken cancellation)
    {
        IDatabaseProvider provider = DatabaseProviders.For(connection.Kind);
        await using DbConnection db = await OpenAsync(provider, connection, password, cancellation).ConfigureAwait(false);
        string version = string.Empty;
        try
        {
            version = db.ServerVersion;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or DbException)
        {
            // not all drivers report it
        }

        return version.Length > 0 ? $"Connected · {connection.Kind.DisplayName()} {version}" : "Connected";
    }

    public static async Task<IReadOnlyList<string>> ListTablesAsync(DataConnectionModel connection, string? password, CancellationToken cancellation)
    {
        IDatabaseProvider provider = DatabaseProviders.For(connection.Kind);
        await using DbConnection db = await OpenAsync(provider, connection, password, cancellation).ConfigureAwait(false);
        try
        {
            return await provider.ListTablesAsync(db, cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or ArgumentException)
        {
            throw new DataSourceException("Connected, but the list of tables could not be read:\n" + ex.Message, ex);
        }
    }

    /// <summary>The statement that will run: the typed query (checked), or the chosen table.</summary>
    public static string BuildSql(DataConnectionModel connection, int maxRows)
    {
        IDatabaseProvider provider = DatabaseProviders.For(connection.Kind);
        if (!string.IsNullOrWhiteSpace(connection.Query))
        {
            QueryCheck check = SqlQueryGuard.Check(connection.Query, provider.Dialect);
            if (!check.IsAllowed)
            {
                throw new DataSourceException(check.Problem!);
            }

            return connection.Query.Trim().TrimEnd(';');
        }

        if (string.IsNullOrWhiteSpace(connection.TableName))
        {
            throw new DataSourceException("Choose a table, or type a SELECT query.");
        }

        return provider.SelectTable(connection.TableName, maxRows);
    }

    /// <summary>
    /// Reads the rows. The first row of the result is the column names, like a header
    /// row in a file. <paramref name="maxRows"/> overrides the connection's own limit
    /// (the connect dialog previews the first rows only).
    /// </summary>
    public static async Task<RawTable> ReadAsync(
        DataConnectionModel connection, string? password, int? maxRows, IProgress<int>? progress, CancellationToken cancellation)
    {
        IDatabaseProvider provider = DatabaseProviders.For(connection.Kind);
        int limit = maxRows ?? connection.MaxRows;
        string sql = BuildSql(connection, limit);

        await using DbConnection db = await OpenAsync(provider, connection, password, cancellation).ConfigureAwait(false);
        DbTransaction? transaction = await BeginReadOnlyAsync(provider, db, cancellation).ConfigureAwait(false);
        try
        {
            await using DbCommand command = db.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = Math.Clamp(connection.CommandTimeoutSeconds, 5, 3600);
            command.Transaction = transaction;

            var rows = new List<IReadOnlyList<string>>();
            await using (DbDataReader reader = await ExecuteAsync(command, cancellation).ConfigureAwait(false))
            {
                var header = new string[reader.FieldCount];
                for (int i = 0; i < header.Length; i++)
                {
                    string name = reader.GetName(i);
                    header[i] = string.IsNullOrWhiteSpace(name) ? $"Column{i + 1}" : name;
                }

                rows.Add(header);

                while (await reader.ReadAsync(cancellation).ConfigureAwait(false))
                {
                    var values = new string[header.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = Format(reader, i);
                    }

                    rows.Add(values);
                    int count = rows.Count - 1;
                    if (count % ProgressEvery == 0)
                    {
                        progress?.Report(count);
                    }

                    if (limit > 0 && count >= limit)
                    {
                        break;
                    }
                }
            }

            progress?.Report(rows.Count - 1);
            return new RawTable { Rows = rows };
        }
        finally
        {
            if (transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is DbException or InvalidOperationException)
                {
                    // Nothing was written; a failed rollback of a read changes nothing.
                }

                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads the source and applies the connection's renames, filters and sort.</summary>
    public static async Task<DataSet> LoadDataSetAsync(
        DataConnectionModel connection, string? password, IProgress<int>? progress, CancellationToken cancellation)
    {
        RawTable raw = await ReadAsync(connection, password, null, progress, cancellation).ConfigureAwait(false);

        // A result set's header is always its first row, whatever a file source used.
        DataConnectionModel shape = connection.Clone();
        shape.HeaderRowIndex = 0;
        return new DataImportService().BuildDataSet(raw, shape);
    }

    /// <summary>Text for one cell, the way it should appear on a label.</summary>
    internal static string Format(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return string.Empty;
        }

        object value;
        try
        {
            value = reader.GetValue(ordinal);
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException or NotSupportedException)
        {
            // Values .NET cannot represent (dates before year 1, huge decimals): fall back to the driver's text.
            try
            {
                return reader.GetString(ordinal);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        return FormatValue(value);
    }

    public static string FormatValue(object? value) => value switch
    {
        null or DBNull => string.Empty,
        string s => s,
        DateTime d => d.TimeOfDay == TimeSpan.Zero
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : d.ToString(d.Millisecond == 0 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        DateTimeOffset o => o.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        byte[] bytes => $"(binary, {bytes.Length:N0} bytes)",
        Guid g => g.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static async Task<DbConnection> OpenAsync(
        IDatabaseProvider provider, DataConnectionModel connection, string? password, CancellationToken cancellation)
    {
        DbConnection db = provider.CreateConnection(connection, password);
        try
        {
            await db.OpenAsync(cancellation).ConfigureAwait(false);
            return db;
        }
        catch (OperationCanceledException)
        {
            await db.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or ArgumentException or
                                       System.Net.Sockets.SocketException or TimeoutException or PlatformNotSupportedException)
        {
            await db.DisposeAsync().ConfigureAwait(false);
            throw new DataSourceException(Explain(connection, ex), ex);
        }
    }

    private static async Task<DbDataReader> ExecuteAsync(DbCommand command, CancellationToken cancellation)
    {
        try
        {
            return await command.ExecuteReaderAsync(CommandBehavior.Default, cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            throw new DataSourceException("The database refused the query:\n" + ex.Message, ex);
        }
    }

    /// <summary>
    /// A transaction the read runs in and that is always rolled back; where the database
    /// can enforce it, the session or transaction is also declared read-only.
    /// </summary>
    private static async Task<DbTransaction?> BeginReadOnlyAsync(IDatabaseProvider provider, DbConnection db, CancellationToken cancellation)
    {
        try
        {
            if (provider.ReadOnlySessionSql is { } session)
            {
                await RunAsync(db, null, session, cancellation).ConfigureAwait(false);
            }

            DbTransaction transaction = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellation).ConfigureAwait(false);
            if (provider.ReadOnlyTransactionSql is { } readOnly)
            {
                await RunAsync(db, transaction, readOnly, cancellation).ConfigureAwait(false);
            }

            return transaction;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // Some ODBC drivers and Access in read mode have no transactions; the query guard still applies.
            return null;
        }
    }

    private static async Task RunAsync(DbConnection db, DbTransaction? transaction, string sql, CancellationToken cancellation)
    {
        await using DbCommand command = db.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
    }

    private static string Explain(DataConnectionModel connection, Exception ex)
    {
        string message = ex.Message;
        if (connection.Kind == DataSourceKind.Access &&
            (message.Contains("not registered", StringComparison.OrdinalIgnoreCase) || ex is InvalidOperationException))
        {
            return "Access databases need the Microsoft Access Database Engine (64-bit) installed on this PC.\n\n" + message;
        }

        if (connection.Kind == DataSourceKind.Odbc && message.Contains("IM002", StringComparison.OrdinalIgnoreCase))
        {
            return "The ODBC data source or driver was not found. Check the name in Windows' ODBC Data Sources (64-bit).\n\n" + message;
        }

        return $"Could not connect to {connection.Kind.DisplayName()}:\n\n{message}";
    }
}
