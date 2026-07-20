using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using Spectre.Console;

namespace SqlServerMcp.TestClient.Ui;

/// <summary>
/// Interactive, schema-driven menu that lets the user pick a tool, supply its arguments,
/// invoke it, and view the result.
/// </summary>
public sealed class ToolMenu
{
    private const string ViewSchema = "View tool schemas";
    private const string Exit = "Exit";

    private readonly McpClient _client;
    private readonly IReadOnlyList<McpClientTool> _tools;

    public ToolMenu(McpClient client, IReadOnlyList<McpClientTool> tools)
    {
        _client = client;
        _tools = tools;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var choice = AnsiConsole.Prompt(BuildMenu());

            if (choice == Exit)
            {
                return;
            }

            if (choice == ViewSchema)
            {
                ShowSchemas();
                continue;
            }

            var tool = _tools.First(t => t.Name == choice);
            await InvokeToolAsync(tool, cancellationToken).ConfigureAwait(false);
        }
    }

    private SelectionPrompt<string> BuildMenu()
    {
        var prompt = new SelectionPrompt<string>()
            .Title("Select a [green]tool[/] to run:")
            .PageSize(20)
            .MoreChoicesText("[grey](Move up and down to reveal more tools)[/]");

        foreach (var tool in _tools)
        {
            prompt.AddChoice(tool.Name);
        }

        prompt.AddChoices(ViewSchema, Exit);
        return prompt;
    }

    private async Task InvokeToolAsync(McpClientTool tool, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Rule($"[yellow]{Markup.Escape(tool.Name)}[/]").LeftJustified());
        if (!string.IsNullOrWhiteSpace(tool.Description))
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(tool.Description)}[/]");
        }

        var arguments = PromptArguments(tool);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await AnsiConsole.Status()
                .StartAsync("Calling tool...", _ =>
                    _client.CallToolAsync(tool.Name, arguments, cancellationToken: cancellationToken).AsTask())
                .ConfigureAwait(false);
            stopwatch.Stop();

            ResultRenderer.Render(result);
            AnsiConsole.MarkupLine($"[grey]Completed in {stopwatch.ElapsedMilliseconds} ms.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Call failed:[/] {Markup.Escape(ex.Message)}");
        }

        AnsiConsole.WriteLine();
    }

    private static Dictionary<string, object?> PromptArguments(McpClientTool tool)
    {
        var arguments = new Dictionary<string, object?>();
        var schema = tool.JsonSchema;

        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return arguments;
        }

        var required = ReadRequired(schema);

        foreach (var property in properties.EnumerateObject())
        {
            var name = property.Name;
            var propSchema = property.Value;
            var type = ReadType(propSchema);
            var description = propSchema.TryGetProperty("description", out var d) ? d.GetString() : null;
            var isRequired = required.Contains(name);

            var value = PromptValue(name, type, description, isRequired);
            if (value is not null)
            {
                arguments[name] = value;
            }
        }

        return arguments;
    }

    private static object? PromptValue(string name, string type, string? description, bool isRequired)
    {
        var label = $"[green]{Markup.Escape(name)}[/] [grey]({type})[/]";
        if (isRequired)
        {
            label += " [red]*[/]";
        }
        if (!string.IsNullOrWhiteSpace(description))
        {
            label += $"\n  [grey]{Markup.Escape(description)}[/]";
        }
        if (!isRequired)
        {
            label += "\n  [grey](press Enter to skip / use server default)[/]";
        }

        var prompt = new TextPrompt<string>($"{label}\n>")
        {
            AllowEmpty = !isRequired,
        };
        prompt.Validate(input => Validate(input, type, isRequired));

        var raw = AnsiConsole.Prompt(prompt);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Convert(raw.Trim(), type);
    }

    private static ValidationResult Validate(string input, string type, bool isRequired)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return isRequired
                ? ValidationResult.Error("[red]A value is required.[/]")
                : ValidationResult.Success();
        }

        return type switch
        {
            "integer" => long.TryParse(input.Trim(), out _)
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]Enter a whole number.[/]"),
            "number" => double.TryParse(input.Trim(), out _)
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]Enter a number.[/]"),
            "boolean" => TryParseBool(input.Trim(), out _)
                ? ValidationResult.Success()
                : ValidationResult.Error("[red]Enter true/false (or y/n).[/]"),
            _ => ValidationResult.Success(),
        };
    }

    private static object Convert(string input, string type) => type switch
    {
        "integer" => long.Parse(input),
        "number" => double.Parse(input),
        "boolean" => TryParseBool(input, out var b) && b,
        _ => input,
    };

    private static bool TryParseBool(string input, out bool value)
    {
        switch (input.ToLowerInvariant())
        {
            case "true" or "t" or "yes" or "y" or "1":
                value = true;
                return true;
            case "false" or "f" or "no" or "n" or "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static HashSet<string> ReadRequired(JsonElement schema)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in req.EnumerateArray())
            {
                if (item.GetString() is { } name)
                {
                    required.Add(name);
                }
            }
        }

        return required;
    }

    private static string ReadType(JsonElement propSchema)
    {
        if (propSchema.TryGetProperty("type", out var typeElement))
        {
            switch (typeElement.ValueKind)
            {
                case JsonValueKind.String:
                    return typeElement.GetString() ?? "string";
                case JsonValueKind.Array:
                    // e.g. ["integer", "null"] — pick the first non-null type.
                    foreach (var item in typeElement.EnumerateArray())
                    {
                        var value = item.GetString();
                        if (value is not null && value != "null")
                        {
                            return value;
                        }
                    }
                    break;
            }
        }

        return "string";
    }

    private void ShowSchemas()
    {
        foreach (var tool in _tools)
        {
            AnsiConsole.Write(new Rule($"[yellow]{Markup.Escape(tool.Name)}[/]").LeftJustified());
            if (!string.IsNullOrWhiteSpace(tool.Description))
            {
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(tool.Description)}[/]");
            }

            var schema = tool.JsonSchema.GetRawText();
            var pretty = JsonSerializer.Serialize(
                JsonDocument.Parse(schema).RootElement,
                new JsonSerializerOptions { WriteIndented = true });
            AnsiConsole.Write(new Panel(new Text(pretty)) { Border = BoxBorder.Rounded });
        }

        AnsiConsole.WriteLine();
    }
}
