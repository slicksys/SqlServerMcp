namespace SqlServerMcp.Server.Configuration;

/// <summary>
/// Options controlling the SQL Server MCP server's connection and safety limits.
/// Bind from configuration section "SqlServerMcp" (appsettings.json, environment
/// variables prefixed SQLSERVERMCP_, or user secrets).
/// </summary>
public sealed class SqlServerMcpOptions
{
    public const string SectionName = "SqlServerMcp";

    /// <summary>
    /// SQL Server connection string, used directly if set. Prefer <see cref="ConnectionStringName"/>
    /// (pointing at a named entry under the standard "ConnectionStrings" section) instead of putting
    /// the literal connection string here. Can also be supplied via the SQLSERVERMCP_CONNECTIONSTRING
    /// environment variable, which takes precedence over everything else.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Name of an entry under the standard ASP.NET Core "ConnectionStrings" configuration section
    /// (appsettings.json, appsettings.{Environment}.json, or user secrets) to use as the connection
    /// string. Ignored if <see cref="ConnectionString"/> or SQLSERVERMCP_CONNECTIONSTRING is set.
    /// </summary>
    public string? ConnectionStringName { get; set; }

    /// <summary>Default number of rows returned by the table preview tool.</summary>
    public int DefaultPreviewRows { get; set; } = 10;

    /// <summary>Hard cap on rows returned by the table preview tool.</summary>
    public int MaxPreviewRows { get; set; } = 200;

    /// <summary>Hard cap on rows returned by the ad-hoc read-only query tool.</summary>
    public int MaxQueryRows { get; set; } = 500;

    /// <summary>Command timeout, in seconds, applied to every SQL command issued by the server.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
