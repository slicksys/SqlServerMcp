using System.Text.RegularExpressions;

namespace SqlServerMcp.Server.Data;

/// <summary>
/// Best-effort static validation that a user/agent-supplied SQL string is a single,
/// read-only SELECT/CTE statement. This is a safety net, not a security boundary:
/// the SQL login used by this server should also be granted read-only permissions
/// (e.g. db_datareader) at the database level.
/// </summary>
public static class ReadOnlyQueryGuard
{
    private static readonly Regex StripStringsAndComments = new(
        @"'(?:[^']|'')*'|--.*?$|/\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

    private static readonly string[] ForbiddenKeywords =
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "EXEC", "EXECUTE", "GRANT", "REVOKE", "DENY", "sp_", "xp_", "OPENROWSET",
        "OPENDATASOURCE", "OPENQUERY", "BULK", "INTO", "BACKUP", "RESTORE",
        "SHUTDOWN", "DBCC", "ALTER DATABASE"
    };

    public static bool TryValidate(string sql, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            errorMessage = "Query text is empty.";
            return false;
        }

        var cleaned = StripStringsAndComments.Replace(sql, " ");

        // Reject multiple statements. Allow at most one trailing semicolon.
        var withoutTrailingSemicolon = cleaned.TrimEnd().TrimEnd(';');
        if (withoutTrailingSemicolon.Contains(';'))
        {
            errorMessage = "Only a single SQL statement is allowed (no semicolon-separated batches).";
            return false;
        }

        var trimmed = cleaned.TrimStart();
        var startsWithSelect = trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
        var startsWithCte = trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
        if (!startsWithSelect && !startsWithCte)
        {
            errorMessage = "Only SELECT statements (optionally starting with a WITH/CTE clause) are allowed.";
            return false;
        }

        foreach (var keyword in ForbiddenKeywords)
        {
            var pattern = keyword.EndsWith('_')
                ? $@"\b{Regex.Escape(keyword)}\w*"
                : $@"\b{Regex.Escape(keyword)}\b";

            if (Regex.IsMatch(cleaned, pattern, RegexOptions.IgnoreCase))
            {
                errorMessage = $"Query contains disallowed keyword '{keyword.TrimEnd('_')}'. Only read-only SELECT queries are permitted.";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }
}
