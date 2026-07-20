namespace SqlServerMcp.TestClient.Configuration;

/// <summary>
/// Resolved settings that describe how the test client should reach the SqlServerMcp server.
/// </summary>
public sealed class ClientOptions
{
    /// <summary>"stdio" launches the server as a child process; "http" connects to a running server.</summary>
    public string Transport { get; set; } = "stdio";

    public StdioOptions Stdio { get; set; } = new();

    public HttpOptions Http { get; set; } = new();

    public sealed class StdioOptions
    {
        /// <summary>Executable to launch (e.g. "dotnet" or a published SqlServerMcp.Server.exe path).</summary>
        public string Command { get; set; } = "dotnet";

        /// <summary>Arguments passed to <see cref="Command"/>. Must ultimately include "--stdio".</summary>
        public List<string> Arguments { get; set; } = new();

        /// <summary>Optional working directory for the launched server process.</summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Connection string forwarded to the server as the SQLSERVERMCP_CONNECTIONSTRING environment variable.
        /// Leave empty to let the server resolve its own configuration/user secrets.
        /// </summary>
        public string? ConnectionString { get; set; }
    }

    public sealed class HttpOptions
    {
        /// <summary>MCP endpoint URL, e.g. http://localhost:5199/mcp.</summary>
        public string Url { get; set; } = "http://localhost:5199/mcp";
    }

    public bool UseHttp => string.Equals(Transport, "http", StringComparison.OrdinalIgnoreCase);
}
