using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SqlServerMcp.Server.Configuration;

namespace SqlServerMcp.Server.Data;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
    int CommandTimeoutSeconds { get; }
}

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly SqlServerMcpOptions _options;

    public SqlConnectionFactory(IOptions<SqlServerMcpOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                "No SQL Server connection string configured. Set the SQLSERVERMCP_CONNECTIONSTRING " +
                "environment variable or SqlServerMcp:ConnectionString in appsettings.json / user secrets.");
        }
    }

    public int CommandTimeoutSeconds => _options.CommandTimeoutSeconds;

    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
