using ModelContextProtocol.Client;
using SqlServerMcp.TestClient.Configuration;

namespace SqlServerMcp.TestClient.Mcp;

/// <summary>
/// Creates an <see cref="McpClient"/> for the configured transport.
/// </summary>
public static class McpConnectionFactory
{
    public static Task<McpClient> ConnectAsync(ClientOptions options, CancellationToken cancellationToken)
    {
        IClientTransport transport = options.UseHttp
            ? BuildHttpTransport(options)
            : BuildStdioTransport(options);

        return McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static IClientTransport BuildStdioTransport(ClientOptions options)
    {
        Dictionary<string, string?>? env = null;
        if (!string.IsNullOrWhiteSpace(options.Stdio.ConnectionString))
        {
            env = new Dictionary<string, string?>
            {
                ["SQLSERVERMCP_CONNECTIONSTRING"] = options.Stdio.ConnectionString,
            };
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "SqlServerMcp",
            Command = options.Stdio.Command,
            Arguments = options.Stdio.Arguments,
            WorkingDirectory = options.Stdio.WorkingDirectory,
            EnvironmentVariables = env,
        });
    }

    private static IClientTransport BuildHttpTransport(ClientOptions options)
    {
        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(options.Http.Url),
        });
    }
}
