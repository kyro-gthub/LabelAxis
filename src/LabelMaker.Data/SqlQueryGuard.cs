using System.Text;

namespace LabelMaker.Data;

/// <summary>
/// Which quoting and comment rules to read a query with. They differ in ways that
/// matter here: to MySQL <c>#</c> starts a comment and <c>\'</c> is a quote inside a
/// string, while to PostgreSQL <c>#</c> is an operator and to SQL Server a backslash is
/// just a character. Reading with the wrong rules could hide a second statement.
/// </summary>
public enum SqlDialect
{
    /// <summary>SQL Server, SQLite, ODBC, Access: standard quoting only.</summary>
    Standard = 0,

    MySql = 1,

    PostgreSql = 2,
}

/// <summary>Result of checking a query before it is sent.</summary>
public sealed record QueryCheck(bool IsAllowed, string? Problem)
{
    public static QueryCheck Allowed { get; } = new(true, null);

    public static QueryCheck Refused(string problem) => new(false, problem);
}

/// <summary>
/// LabelAxis only ever reads data. A typed query must be a single SELECT (or a
/// WITH … SELECT); anything that could change or lock data is refused before it
/// reaches the server. Comments and quoted text are ignored while checking, so a
/// product called 'Drop-in hinge' is fine and <c>SELECT 1; DROP TABLE x</c> is not.
/// <para>
/// This is one layer: queries also run inside a transaction that is always rolled
/// back, SQLite files open read-only, and a read-only database login is still the
/// right way to connect.
/// </para>
/// </summary>
public static class SqlQueryGuard
{
    // The first word must already be SELECT or WITH and only one statement may run, so
    // these are the ways a SELECT can still write, lock, reach outside the database or
    // stall the server. Kept short on purpose: ordinary column names such as Comment,
    // Use or Set must still work.
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "CREATE", "TRUNCATE", "EXEC", "EXECUTE",
        "CALL", "GRANT", "REVOKE", "INTO", "ATTACH", "DETACH", "PRAGMA", "OUTFILE", "DUMPFILE", "LOAD_FILE",
        "OPENROWSET", "OPENDATASOURCE", "OPENQUERY", "WAITFOR", "SHUTDOWN", "DBCC", "PG_SLEEP", "SLEEP",
        "BENCHMARK", "LO_IMPORT", "LO_EXPORT", "PG_READ_FILE", "PG_TERMINATE_BACKEND", "DBLINK", "NEXTVAL", "SETVAL",
    };

    public static QueryCheck Check(string? sql, SqlDialect dialect = SqlDialect.Standard)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return QueryCheck.Refused("The query is empty.");
        }

        string? error = Strip(sql, dialect, out string code);
        if (error is not null)
        {
            return QueryCheck.Refused(error);
        }

        string trimmed = code.Trim();

        // One statement only; a single trailing semicolon is harmless.
        int semicolon = trimmed.IndexOf(';');
        if (semicolon >= 0 && trimmed[(semicolon + 1)..].Trim().Length > 0)
        {
            return QueryCheck.Refused("Only one statement can be run. Remove everything after the first ';'.");
        }

        List<string> words = Words(trimmed);
        if (words.Count == 0)
        {
            return QueryCheck.Refused("The query is empty.");
        }

        if (!words[0].Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !words[0].Equals("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return QueryCheck.Refused("Only SELECT queries can be used to read label data.");
        }

        if (!words.Any(w => w.Equals("SELECT", StringComparison.OrdinalIgnoreCase)))
        {
            return QueryCheck.Refused("The query must select rows.");
        }

        for (int i = 0; i < words.Count; i++)
        {
            string word = words[i];
            if (Forbidden.Contains(word))
            {
                return QueryCheck.Refused($"\"{word.ToUpperInvariant()}\" is not allowed. Label data is read-only: use a plain SELECT query.");
            }

            if (word.Equals("FOR", StringComparison.OrdinalIgnoreCase) && i + 1 < words.Count &&
                words[i + 1].Equals("SHARE", StringComparison.OrdinalIgnoreCase))
            {
                return QueryCheck.Refused("Row-locking clauses are not allowed.");
            }
        }

        return QueryCheck.Allowed;
    }

    /// <summary>Replaces comments with a space and quoted text with a neutral token, so only the SQL itself is checked.</summary>
    private static string? Strip(string sql, SqlDialect dialect, out string code)
    {
        code = string.Empty;
        var sb = new StringBuilder(sql.Length);
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if ((c == '-' && next == '-') || (c == '#' && dialect == SqlDialect.MySql))
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                sb.Append(' ');
                continue;
            }

            if (c == '/' && next == '*')
            {
                int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    return "A /* comment */ is not closed.";
                }

                // MySQL runs /*! ... */ as code, and PostgreSQL nests comments; refuse both rather than guess.
                if (i + 2 < sql.Length && sql[i + 2] == '!')
                {
                    return "Executable comments (/*! … */) are not allowed.";
                }

                if (sql.IndexOf("/*", i + 2, end - (i + 2), StringComparison.Ordinal) >= 0)
                {
                    return "Nested comments are not allowed.";
                }

                i = end + 2;
                sb.Append(' ');
                continue;
            }

            bool opensQuote = c is '\'' or '"' ||
                              (c == '`' && dialect == SqlDialect.MySql) ||
                              (c == '[' && dialect == SqlDialect.Standard);
            if (opensQuote)
            {
                char close = c == '[' ? ']' : c;
                bool backslashEscapes = dialect == SqlDialect.MySql && c is '\'' or '"';
                int j = i + 1;
                bool closed = false;
                while (j < sql.Length)
                {
                    if (backslashEscapes && sql[j] == '\\' && j + 1 < sql.Length)
                    {
                        j += 2;
                        continue;
                    }

                    if (sql[j] == close)
                    {
                        // A doubled quote is a quote inside the text (also ]] inside [ ]).
                        if (j + 1 < sql.Length && sql[j + 1] == close)
                        {
                            j += 2;
                            continue;
                        }

                        closed = true;
                        break;
                    }

                    j++;
                }

                if (!closed)
                {
                    return "A quote in the query is not closed.";
                }

                sb.Append(" _q ");
                i = j + 1;
                continue;
            }

            if (c == '$' && dialect == SqlDialect.PostgreSql && TryDollarQuote(sql, i, out int after))
            {
                sb.Append(" _q ");
                i = after;
                continue;
            }

            sb.Append(c);
            i++;
        }

        code = sb.ToString();
        return null;
    }

    /// <summary>PostgreSQL $tag$ … $tag$ strings.</summary>
    private static bool TryDollarQuote(string sql, int start, out int after)
    {
        after = start;
        int tagEnd = sql.IndexOf('$', start + 1);
        if (tagEnd < 0)
        {
            return false;
        }

        string tag = sql[start..(tagEnd + 1)];
        if (tag.Length > 2 && !(char.IsLetter(tag[1]) || tag[1] == '_'))
        {
            return false;
        }

        if (!tag[1..^1].All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
        {
            return false;
        }

        int close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        after = close + tag.Length;
        return true;
    }

    private static List<string> Words(string code)
    {
        var words = new List<string>();
        int i = 0;
        while (i < code.Length)
        {
            if (char.IsLetter(code[i]) || code[i] == '_')
            {
                int start = i;
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] is '_' or '$'))
                {
                    i++;
                }

                words.Add(code[start..i]);
            }
            else
            {
                i++;
            }
        }

        return words;
    }
}
