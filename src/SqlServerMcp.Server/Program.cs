using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlServerMcp.Server.Configuration;
using SqlServerMcp.Server.Data;

var useStdio = args.Contains("--stdio", StringComparer.OrdinalIgnoreCase);

if (useStdio)
{
    await RunStdioServerAsync(args).ConfigureAwait(false);
}
else
{
    RunHttpServer(args);
}

return;

static void ConfigureSharedServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddOptions<SqlServerMcpOptions>()
        .Bind(configuration.GetSection(SqlServerMcpOptions.SectionName))
        .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
            "No connection string resolved. Set SQLSERVERMCP_CONNECTIONSTRING, or SqlServerMcp:ConnectionString, " +
            "or SqlServerMcp:ConnectionStringName pointing at an entry under ConnectionStrings.");

    services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
}

static async Task RunStdioServerAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Keep stdout reserved exclusively for MCP JSON-RPC traffic; send all logs to stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    if (builder.Environment.IsDevelopment())
    {
        builder.Configuration.AddUserSecrets("sqlservermcp-server");
    }

    ApplyConnectionStringOverride(builder.Configuration);
    ConfigureSharedServices(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var host = builder.Build();
    await host.RunAsync().ConfigureAwait(false);
}

static void RunHttpServer(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    if (builder.Environment.IsDevelopment())
    {
        builder.Configuration.AddUserSecrets("sqlservermcp-server");
    }

    ApplyConnectionStringOverride(builder.Configuration);
    ConfigureSharedServices(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();

    app.MapGet("/", () => "SQL Server MCP Server is running. MCP endpoint: /mcp");
    app.MapMcp("/mcp");

    app.Run();
}

/// <summary>
/// Resolves the effective connection string into SqlServerMcp:ConnectionString, in priority order:
/// 1. SQLSERVERMCP_CONNECTIONSTRING environment variable (highest precedence).
/// 2. SqlServerMcp:ConnectionString, if already set in appsettings/user secrets.
/// 3. ConnectionStrings:{SqlServerMcp:ConnectionStringName}, the standard ASP.NET Core convention,
///    letting multiple named connections live under "ConnectionStrings" and be selected by name.
/// </summary>
static void ApplyConnectionStringOverride(IConfiguration configuration)
{
    var envConnectionString = Environment.GetEnvironmentVariable("SQLSERVERMCP_CONNECTIONSTRING");
    if (!string.IsNullOrWhiteSpace(envConnectionString))
    {
        configuration[$"{SqlServerMcpOptions.SectionName}:ConnectionString"] = envConnectionString;
        return;
    }

    var explicitConnectionString = configuration[$"{SqlServerMcpOptions.SectionName}:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(explicitConnectionString))
    {
        return;
    }

    var connectionStringName = configuration[$"{SqlServerMcpOptions.SectionName}:ConnectionStringName"];
    if (!string.IsNullOrWhiteSpace(connectionStringName))
    {
        var namedConnectionString = configuration.GetConnectionString(connectionStringName);
        if (!string.IsNullOrWhiteSpace(namedConnectionString))
        {
            configuration[$"{SqlServerMcpOptions.SectionName}:ConnectionString"] = namedConnectionString;
        }
    }
}
