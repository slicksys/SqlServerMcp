using Microsoft.Data.SqlClient;

namespace SqlServerMcp.Server.Data;

internal static class SqlRowReader
{
    /// <summary>
    /// Reads up to <paramref name="maxRows"/> rows from the reader into a list of
    /// column-name/value dictionaries, JSON-friendly (DBNull -> null).
    /// </summary>
    public static async Task<List<Dictionary<string, object?>>> ReadRowsAsync(
        SqlDataReader reader, int maxRows, CancellationToken cancellationToken)
    {
        var results = new List<Dictionary<string, object?>>();
        var columnNames = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columnNames[i] = reader.GetName(i);
        }

        while (results.Count < maxRows && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[columnNames[i]] = value is DBNull ? null : value;
            }
            results.Add(row);
        }

        return results;
    }

    public static string[] GetColumnNames(SqlDataReader reader)
    {
        var names = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            names[i] = reader.GetName(i);
        }
        return names;
    }
}
