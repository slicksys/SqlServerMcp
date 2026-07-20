using System.ComponentModel;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;
using SqlServerMcp.Server.Data;

namespace SqlServerMcp.Server.Tools;

[McpServerToolType]
public sealed class SchemaExplorerTools(ISqlConnectionFactory connectionFactory)
{
    [McpServerTool(Name = "list_schemas"),
     Description("Lists the database schemas (e.g. dbo, sales, hr). Use this first to " +
                  "understand how the database is organized before drilling into tables.")]
    public async Task<object> ListSchemasAsync(
        [Description("Include built-in system schemas such as sys, INFORMATION_SCHEMA, guest, db_*. Default false.")]
        bool includeSystemSchemas = false,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.name AS SchemaName, p.name AS OwnerName,
                   (SELECT COUNT(*) FROM sys.tables t WHERE t.schema_id = s.schema_id) AS TableCount
            FROM sys.schemas s
            JOIN sys.database_principals p ON s.principal_id = p.principal_id
            WHERE (@includeSystem = 1 OR s.name NOT IN ('sys', 'INFORMATION_SCHEMA', 'guest')
                   AND s.name NOT LIKE 'db_%')
            ORDER BY s.name
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connectionFactory.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@includeSystem", includeSystemSchemas);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await SqlRowReader.ReadRowsAsync(reader, int.MaxValue, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_tables"),
     Description("Lists tables (and optionally views) in the database, with row counts and any " +
                  "MS_Description extended-property description. Optionally filter to one schema.")]
    public async Task<object> ListTablesAsync(
        [Description("Schema to filter by, e.g. 'dbo'. Omit to list tables across all schemas.")]
        string? schema = null,
        [Description("Include views in addition to tables. Default true.")]
        bool includeViews = true,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                s.name AS SchemaName,
                t.name AS TableName,
                'BASE TABLE' AS ObjectType,
                CAST(SUM(p.rows) AS BIGINT) AS ApproxRowCount,
                ep.value AS Description
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = t.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
            WHERE (@schema IS NULL OR s.name = @schema)
            GROUP BY s.name, t.name, ep.value

            UNION ALL

            SELECT
                s.name AS SchemaName,
                v.name AS TableName,
                'VIEW' AS ObjectType,
                NULL AS ApproxRowCount,
                ep.value AS Description
            FROM sys.views v
            JOIN sys.schemas s ON v.schema_id = s.schema_id
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = v.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description'
            WHERE @includeViews = 1 AND (@schema IS NULL OR s.name = @schema)

            ORDER BY SchemaName, TableName
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connectionFactory.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", (object?)schema ?? DBNull.Value);
        command.Parameters.AddWithValue("@includeViews", includeViews);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await SqlRowReader.ReadRowsAsync(reader, int.MaxValue, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_table_columns"),
     Description("Gets full column detail for a table: data type, length/precision, nullability, " +
                  "default, identity, primary-key flag, and any MS_Description on the column.")]
    public async Task<object> GetTableColumnsAsync(
        [Description("Schema name, e.g. 'dbo'.")] string schema,
        [Description("Table name.")] string table,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                c.COLUMN_NAME AS ColumnName,
                c.ORDINAL_POSITION AS OrdinalPosition,
                c.DATA_TYPE AS DataType,
                c.CHARACTER_MAXIMUM_LENGTH AS MaxLength,
                c.NUMERIC_PRECISION AS NumericPrecision,
                c.NUMERIC_SCALE AS NumericScale,
                c.IS_NULLABLE AS IsNullable,
                c.COLUMN_DEFAULT AS DefaultValue,
                CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)),
                     c.COLUMN_NAME, 'IsIdentity') AS BIT) AS IsIdentity,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsPrimaryKey,
                ep.value AS Description
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA AND pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME))
                AND ep.minor_id = c.ORDINAL_POSITION AND ep.name = 'MS_Description'
            WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
            ORDER BY c.ORDINAL_POSITION
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connectionFactory.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = await SqlRowReader.ReadRowsAsync(reader, int.MaxValue, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return new { Error = $"No columns found. Verify schema '{schema}' and table '{table}' exist (see list_tables)." };
        }
        return rows;
    }

    [McpServerTool(Name = "get_table_relationships"),
     Description("Gets foreign-key relationships for a table, in both directions: FKs this table " +
                  "declares to other tables, and FKs on other tables that reference this table.")]
    public async Task<object> GetTableRelationshipsAsync(
        [Description("Schema name, e.g. 'dbo'.")] string schema,
        [Description("Table name.")] string table,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                fk.name AS ForeignKeyName,
                s1.name AS FromSchema, t1.name AS FromTable, c1.name AS FromColumn,
                s2.name AS ToSchema, t2.name AS ToTable, c2.name AS ToColumn,
                fk.delete_referential_action_desc AS OnDelete,
                fk.update_referential_action_desc AS OnUpdate,
                CASE WHEN s1.name = @schema AND t1.name = @table THEN 'Outgoing' ELSE 'Incoming' END AS Direction
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.tables t1 ON fk.parent_object_id = t1.object_id
            JOIN sys.schemas s1 ON t1.schema_id = s1.schema_id
            JOIN sys.columns c1 ON fkc.parent_object_id = c1.object_id AND fkc.parent_column_id = c1.column_id
            JOIN sys.tables t2 ON fk.referenced_object_id = t2.object_id
            JOIN sys.schemas s2 ON t2.schema_id = s2.schema_id
            JOIN sys.columns c2 ON fkc.referenced_object_id = c2.object_id AND fkc.referenced_column_id = c2.column_id
            WHERE (s1.name = @schema AND t1.name = @table) OR (s2.name = @schema AND t2.name = @table)
            ORDER BY Direction, fk.name
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connectionFactory.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await SqlRowReader.ReadRowsAsync(reader, int.MaxValue, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_table_indexes"),
     Description("Gets indexes for a table, including the primary key and unique constraints, " +
                  "key columns (in order) and included columns.")]
    public async Task<object> GetTableIndexesAsync(
        [Description("Schema name, e.g. 'dbo'.")] string schema,
        [Description("Table name.")] string table,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                i.name AS IndexName,
                i.type_desc AS IndexType,
                i.is_unique AS IsUnique,
                i.is_primary_key AS IsPrimaryKey,
                i.is_unique_constraint AS IsUniqueConstraint,
                STRING_AGG(CASE WHEN ic.is_included_column = 0
                                THEN c.name + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END
                           END, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns,
                STRING_AGG(CASE WHEN ic.is_included_column = 1 THEN c.name END, ', ') AS IncludedColumns
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE s.name = @schema AND t.name = @table AND i.type > 0
            GROUP BY i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint
            ORDER BY i.is_primary_key DESC, i.name
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connectionFactory.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await SqlRowReader.ReadRowsAsync(reader, int.MaxValue, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "search_schema"),
     Description("Full-text style search across table names, column names, and extended-property " +
                  "descriptions (MS_Description) on tables and columns. Use this to quickly find " +
                  "which tables/columns are relevant to a business concept, e.g. 'invoice' or 'customer'.")]
    public async Task<object> SearchSchemaAsync(
        [Description("Search term. Matched as a case-insensitive substring (SQL LIKE '%term%').")]
        string searchTerm,
        [Description("Maximum number of matches to return. Default 100.")]
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return new { Error = "searchTerm must not be empty." };
        }

        const string sql = """
            SELECT TOP (@maxResults) MatchType, SchemaName, TableName, ColumnName, Description FROM (
                SELECT 'Table' AS MatchType, s.name AS SchemaName, t.name AS TableName,
                       CAST(NULL AS sysname) AS ColumnName, CAST(NULL AS NVARCHAR(MAX)) AS Description
                FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.name LIKE @pattern

                UNION ALL

                SELECT 'Column', s.name, t.name, c.name, CAST(NULL AS NVARCHAR(MAX))
                FROM sys.columns c
                JOIN sys.tables t ON c.object_id = t.object_id
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE c.name LIKE @pattern

                UNION ALL

                SELECT 'TableDescription', s.name, t.name, CAST(NULL AS sysname), CAST(ep.value AS NVARCHAR(MAX))
                FROM sys.extended_properties ep
                JOIN sys.tables t ON ep.major_id = t.object_id AND ep.minor_id = 0
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE ep.name = 'MS_Description' AND CAST(ep.value AS NVARCHAR(MAX)) LIKE @pattern

                UNION ALL

                SELECT 'ColumnDescription', s.name, t.name, c.name, CAST(ep.value AS NVARCHAR(MAX))
                FROM sys.extended_properties ep
                JOIN sys.columns c ON ep.major_id = c.object_id AND ep.minor_id = c.column_id
                JOIN sys.tables t ON c.object_id = t.object_id
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE ep.name = 'MS_Description' AND CAST(ep.value AS NVARCHAR(MAX)) LIKE @pattern
            ) matches
            ORDER BY SchemaName, TableName, MatchType
            """;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = connectionFactory.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@pattern", $"%{searchTerm}%");
        command.Parameters.AddWithValue("@maxResults", Math.Clamp(maxResults, 1, 1000));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await SqlRowReader.ReadRowsAsync(reader, int.MaxValue, cancellationToken).ConfigureAwait(false);
    }
}
