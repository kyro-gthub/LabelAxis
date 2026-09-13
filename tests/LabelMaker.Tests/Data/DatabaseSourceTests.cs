using FluentAssertions;
using LabelMaker.Core.Data.Import;
using LabelMaker.Core.Model;
using LabelMaker.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LabelMaker.Tests.Data;

public sealed class DatabaseSourceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"lm-db-{Guid.NewGuid():N}.sqlite");

    public DatabaseSourceTests()
    {
        using var db = new SqliteConnection($"Data Source={_file};Pooling=False");
        db.Open();
        using SqliteCommand create = db.CreateCommand();
        create.CommandText = """
            CREATE TABLE Products (Sku TEXT, Name TEXT, Price REAL, Stock INTEGER, Added TEXT, Comment TEXT);
            INSERT INTO Products VALUES ('A-001', 'Drop-in hinge', 4.5, 10, '2026-01-02', NULL);
            INSERT INTO Products VALUES ('A-002', 'Shelf bracket', 12.25, 0, '2026-02-03', 'Update pending');
            INSERT INTO Products VALUES ('B-001', 'Wall plug', 0.1, 500, '2026-03-04', 'x');
            CREATE VIEW InStock AS SELECT Sku, Name FROM Products WHERE Stock > 0;
            """;
        create.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
        }
    }

    private DataConnectionModel Connection(string? table = null, string? query = null) => new()
    {
        Kind = DataSourceKind.Sqlite,
        SourcePath = _file,
        TableName = table,
        Query = query,
    };

    [Fact]
    public async Task Lists_tables_and_views()
    {
        IReadOnlyList<string> tables = await DatabaseReader.ListTablesAsync(Connection(), null, CancellationToken.None);

        tables.Should().Equal("InStock", "Products");
    }

    [Fact]
    public async Task Reads_a_table_into_records_with_its_column_names()
    {
        var data = await DatabaseReader.LoadDataSetAsync(Connection("Products"), null, null, CancellationToken.None);

        data.Columns.Select(c => c.Name).Should().Equal("Sku", "Name", "Price", "Stock", "Added", "Comment");
        data.RowCount.Should().Be(3);
        data.Rows[0]["Name"].Should().Be("Drop-in hinge");
        data.Rows[1]["Price"].Should().Be("12.25");
        data.Rows[0]["Comment"].Should().BeEmpty();
    }

    [Fact]
    public async Task Runs_a_select_query_and_applies_filters_and_sorting()
    {
        DataConnectionModel connection = Connection(query: "SELECT Sku, Name, Stock FROM Products WHERE Name <> 'Drop; DELETE'");
        connection.Filters.Add(new DataFilter { Column = "Stock", Operator = DataFilterOperator.GreaterThan, Value = "5" });
        connection.SortColumn = "Stock";
        connection.SortDescending = true;
        connection.ColumnRenames["Sku"] = "Code";

        var data = await DatabaseReader.LoadDataSetAsync(connection, null, null, CancellationToken.None);

        data.Columns.Select(c => c.Name).Should().Equal("Code", "Name", "Stock");
        data.Rows.Select(r => r["Code"]).Should().Equal("B-001", "A-001");
    }

    [Fact]
    public async Task Row_limit_stops_reading_early()
    {
        DataConnectionModel connection = Connection("Products");
        connection.MaxRows = 2;

        RawTable raw = await DatabaseReader.ReadAsync(connection, null, null, null, CancellationToken.None);

        raw.RowCount.Should().Be(3, "a header row plus two records");
        (await DatabaseReader.ReadAsync(Connection(query: "SELECT * FROM Products"), null, 1, null, CancellationToken.None))
            .RowCount.Should().Be(2);
    }

    [Fact]
    public async Task Queries_that_write_are_refused_before_reaching_the_database()
    {
        Func<Task> act = () => DatabaseReader.ReadAsync(Connection(query: "DELETE FROM Products"), null, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<DataSourceException>().WithMessage("*SELECT*");
        (await DatabaseReader.LoadDataSetAsync(Connection("Products"), null, null, CancellationToken.None)).RowCount.Should().Be(3);
    }

    [Fact]
    public async Task The_database_file_is_opened_read_only()
    {
        DatabaseReader.BuildSql(Connection("Products"), 0).Should().Be("SELECT * FROM \"Products\"");

        using var db = (SqliteConnection)DatabaseProviders.For(DataSourceKind.Sqlite).CreateConnection(Connection(), null);
        await db.OpenAsync();
        using SqliteCommand write = db.CreateCommand();
        write.CommandText = "DELETE FROM Products";

        Func<Task> act = () => write.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task A_missing_file_is_explained()
    {
        DataConnectionModel connection = Connection("Products");
        connection.SourcePath = Path.Combine(Path.GetTempPath(), "does-not-exist.sqlite");

        Func<Task> act = () => DatabaseReader.TestAsync(connection, null, CancellationToken.None);

        await act.Should().ThrowAsync<DataSourceException>().WithMessage("*not found*");
    }

    [Theory]
    [InlineData("SELECT * FROM Products")]
    [InlineData("  select Sku, Comment, [Use], \"Set\" from Products;  ")]
    [InlineData("WITH cheap AS (SELECT * FROM Products WHERE Price < 5) SELECT * FROM cheap")]
    [InlineData("SELECT REPLACE(Name, 'a', 'b') AS Name FROM Products -- DROP TABLE Products")]
    [InlineData("SELECT 'Update; delete; drop' AS Note /* INSERT */ FROM Products")]
    [InlineData("SELECT [Order Details].Qty FROM [Order Details]")]
    public void Guard_allows_plain_selects(string sql)
    {
        SqlQueryGuard.Check(sql).IsAllowed.Should().BeTrue(SqlQueryGuard.Check(sql).Problem);
    }

    [Theory]
    [InlineData("DELETE FROM Products")]
    [InlineData("SELECT 1; DROP TABLE Products")]
    [InlineData("SELECT * INTO Backup FROM Products")]
    [InlineData("WITH gone AS (DELETE FROM Products RETURNING *) SELECT * FROM gone")]
    [InlineData("EXEC sp_who")]
    [InlineData("SELECT * FROM Products FOR UPDATE")]
    [InlineData("SELECT 'unclosed FROM Products")]
    [InlineData("SELECT 1 /* never closed")]
    [InlineData("")]
    public void Guard_refuses_anything_that_is_not_a_single_read(string sql)
    {
        SqlQueryGuard.Check(sql).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Guard_reads_each_database_with_its_own_quoting_rules()
    {
        // To SQL Server a backslash is an ordinary character, so the string ends at the second quote
        // and a statement follows; to MySQL the quote is escaped and it is all one string.
        const string backslash = @"SELECT 'x\'; DELETE FROM Products; -- ' AS a";
        SqlQueryGuard.Check(backslash, SqlDialect.Standard).IsAllowed.Should().BeFalse();
        SqlQueryGuard.Check(backslash, SqlDialect.MySql).IsAllowed.Should().BeTrue();

        // '#' comments only exist in MySQL; in PostgreSQL it is an operator and cannot hide text.
        const string hash = "SELECT 1 # 2; DELETE FROM Products";
        SqlQueryGuard.Check(hash, SqlDialect.PostgreSql).IsAllowed.Should().BeFalse();

        // MySQL executable comments run their content.
        SqlQueryGuard.Check("SELECT 1 /*! ; DROP TABLE t */", SqlDialect.MySql).IsAllowed.Should().BeFalse();

        // PostgreSQL dollar-quoted text is text.
        SqlQueryGuard.Check("SELECT $$; DROP TABLE t;$$ AS a", SqlDialect.PostgreSql).IsAllowed.Should().BeTrue();
        SqlQueryGuard.Check("SELECT $$; DROP TABLE t;$$ AS a", SqlDialect.Standard).IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void Values_are_formatted_for_labels()
    {
        DatabaseReader.FormatValue(new DateTime(2026, 9, 13)).Should().Be("2026-09-13");
        DatabaseReader.FormatValue(new DateTime(2026, 9, 13, 14, 5, 0)).Should().Be("2026-09-13 14:05:00");
        DatabaseReader.FormatValue(12.5m).Should().Be("12.5");
        DatabaseReader.FormatValue(true).Should().Be("true");
        DatabaseReader.FormatValue(DBNull.Value).Should().BeEmpty();
        DatabaseReader.FormatValue(new byte[] { 1, 2, 3 }).Should().Be("(binary, 3 bytes)");
    }

    [Fact]
    public void Server_connections_quote_names_and_limit_rows_in_their_own_dialect()
    {
        DatabaseProviders.For(DataSourceKind.SqlServer).SelectTable("dbo.Order Items", 50)
            .Should().Be("SELECT TOP (50) * FROM [dbo].[Order Items]");
        DatabaseProviders.For(DataSourceKind.MySql).SelectTable("labels", 10)
            .Should().Be("SELECT * FROM `labels` LIMIT 10");
        DatabaseProviders.For(DataSourceKind.PostgreSql).SelectTable("public.\"weird\"\"name\"", 0)
            .Should().Be("SELECT * FROM \"public\".\"weird\"\"name\"");
    }
}
