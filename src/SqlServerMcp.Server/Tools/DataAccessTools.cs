using System.ComponentModel;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcp.Server.Configuration;
using SqlServerMcp.Server.Data;
using Microsoft.Extensions.Options;

namespace SqlServerMcp.Server.Tools;

[McpServerToolType]
public sealed class DataAccessTools(ISqlConnectionFactory connectionFactory, IOptions<SqlServerMcpOptions> options)
{
    private readonly SqlServerMcpOptions _options = options.Value;

    [McpServerTool(Name = "preview_table_data"),
     Description("Returns a small sample of rows (TOP N, no particular order) from a table or view, " +
                  "to help understand real data shape/values alongside the schema metadata.")]
    public async Task<object> PreviewTableDataAsync(
        [Description("Schema name, e.g. 'dbo'.")] string schema,
        [Description("Table or view name.")] string table,
        [Description("Number of rows to return. Defaults to server setting; capped for safety.")]
        int? topRows = null,
        CancellationToken cancellationToken = default)
    {
        var rowCount = Math.Clamp(topRows ?? _options.DefaultPreviewRows, 1, _options.MaxPreviewRows);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (!await TableExistsAsync(connection, schema, table, cancellationToken).ConfigureAwait(false))
        {
            return new { Error = $"Table or view '{schema}.{table}' was not found. Use list_tables or search_schema to find valid names." };
        }

        var quotedSchema = QuoteIdentifier(schema);
        var quotedTable = QuoteIdentifier(table);
        var sql = $"SELECT TOP (@rowCount) * FROM {quotedSchema}.{quotedTable}";

        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connectionFactory.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@rowCount", rowCount);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = SqlRowReader.GetColumnNames(reader);
        var rows = await SqlRowReader.ReadRowsAsync(reader, rowCount, cancellationToken).ConfigureAwait(false);

        return new { Schema = schema, Table = table, Columns = columns, RowsReturned = rows.Count, Rows = rows };
    }

    [McpServerTool(Name = "execute_readonly_query"),
     Description("Executes an ad-hoc, read-only SQL query (a single SELECT statement, optionally " +
                  "with a leading WITH/CTE clause) against the database and returns the result rows. " +
                  "Any statement that is not a plain SELECT (INSERT/UPDATE/DELETE/DDL/EXEC/etc.) is rejected. " +
                  "Use this for one-off exploratory questions once you already know the relevant tables/columns " +
                  "from list_tables/get_table_columns/search_schema.")]
    public async Task<object> ExecuteReadOnlyQueryAsync(
        [Description("A single read-only SELECT (or WITH ... SELECT) statement. No semicolon-separated batches.")]
        string sql,
        [Description("Maximum number of rows to return. Capped for safety.")]
        int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        if (!ReadOnlyQueryGuard.TryValidate(sql, out var validationError))
        {
            return new { Error = validationError };
        }

        var rowCap = Math.Clamp(maxRows ?? _options.MaxQueryRows, 1, _options.MaxQueryRows);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.ReadUncommitted);

        try
        {
            await using var command = new SqlCommand(sql, connection, transaction)
            {
                CommandTimeout = connectionFactory.CommandTimeoutSeconds
            };

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var columns = SqlRowReader.GetColumnNames(reader);
            var rows = await SqlRowReader.ReadRowsAsync(reader, rowCap, cancellationToken).ConfigureAwait(false);
            var truncated = rows.Count == rowCap;

            return new { Columns = columns, RowsReturned = rows.Count, Truncated = truncated, Rows = rows };
        }
        catch (SqlException ex)
        {
            return new { Error = $"SQL error: {ex.Message}" };
        }
        finally
        {
            try { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); } catch { /* connection closing anyway */ }
        }
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    private static string QuoteIdentifier(string identifier) =>
        "[" + identifier.Replace("]", "]]") + "]";
}
