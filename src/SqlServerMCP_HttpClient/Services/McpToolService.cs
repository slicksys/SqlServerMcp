using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SqlServerMcp.WebClient.Services;

/// <summary>
/// Per-circuit wrapper around an <see cref="McpClient"/> connected to the SqlServerMcp server
/// over HTTP/HTTPS. Mirrors the functionality of the SqlServerMCP_Client console tool (list tools,
/// invoke a tool, view schemas) for use from a Blazor page.
/// </summary>
public sealed class McpToolService : IAsyncDisposable
{
    private McpClient? _client;

    public string? ConnectedUrl { get; private set; }

    public IReadOnlyList<McpClientTool> Tools { get; private set; } = Array.Empty<McpClientTool>();

    public bool IsConnected => _client is not null;

    public async Task ConnectAsync(string url, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(url),
        });

        _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        ConnectedUrl = url;

        try
        {
            var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            Tools = tools.ToList();
        }
        catch
        {
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Not connected to an MCP server.");
        }

        return _client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken).AsTask();
    }

    public async Task DisconnectAsync()
    {
        if (_client is null)
        {
            return;
        }

        await _client.DisposeAsync().ConfigureAwait(false);
        _client = null;
        ConnectedUrl = null;
        Tools = Array.Empty<McpClientTool>();
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());
}
