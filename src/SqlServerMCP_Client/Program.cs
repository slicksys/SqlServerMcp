using ModelContextProtocol.Client;
using Spectre.Console;
using SqlServerMcp.TestClient.Configuration;
using SqlServerMcp.TestClient.Mcp;
using SqlServerMcp.TestClient.Ui;

var options = ConfigLoader.Load(args);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

AnsiConsole.Write(new FigletText("SqlServerMcp").Color(Color.Green));
AnsiConsole.Write(new Rule("[green]MCP Test Client[/]").LeftJustified());

DescribeTarget(options);

McpClient client;
try
{
    client = await AnsiConsole.Status()
        .StartAsync("Connecting to the MCP server...", _ =>
            McpConnectionFactory.ConnectAsync(options, cts.Token))
        .ConfigureAwait(false);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Failed to connect:[/] {Markup.Escape(ex.Message)}");
    if (!options.UseHttp)
    {
        AnsiConsole.MarkupLine("[grey]Check that the server command/project path and connection string are correct.[/]");
    }
    else
    {
        AnsiConsole.MarkupLine("[grey]Check that the server is running and the URL is reachable.[/]");
    }
    return 1;
}

await using (client.ConfigureAwait(false))
{
    IList<McpClientTool> tools;
    try
    {
        tools = await client.ListToolsAsync(cancellationToken: cts.Token).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to list tools:[/] {Markup.Escape(ex.Message)}");
        return 1;
    }

    AnsiConsole.MarkupLine($"[green]Connected.[/] Discovered [yellow]{tools.Count}[/] tool(s).");
    AnsiConsole.WriteLine();

    var menu = new ToolMenu(client, tools.ToList());
    try
    {
        await menu.RunAsync(cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // Graceful shutdown on Ctrl+C.
    }
}

AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
return 0;

static void DescribeTarget(ClientOptions options)
{
    if (options.UseHttp)
    {
        AnsiConsole.MarkupLine($"[grey]Transport:[/] HTTP  [grey]Endpoint:[/] {Markup.Escape(options.Http.Url)}");
    }
    else
    {
        var command = $"{options.Stdio.Command} {string.Join(' ', options.Stdio.Arguments)}";
        AnsiConsole.MarkupLine($"[grey]Transport:[/] stdio  [grey]Command:[/] {Markup.Escape(command)}");
        var hasConnection = !string.IsNullOrWhiteSpace(options.Stdio.ConnectionString);
        AnsiConsole.MarkupLine($"[grey]Connection string:[/] {(hasConnection ? "[green]provided[/]" : "[yellow]not set (server will resolve its own)[/]")}");
    }

    AnsiConsole.WriteLine();
}
