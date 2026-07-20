using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Spectre.Console;

namespace SqlServerMcp.TestClient.Ui;

/// <summary>
/// Renders <see cref="CallToolResult"/> payloads. Row-shaped JSON is shown as a table;
/// everything else is pretty-printed JSON.
/// </summary>
public static class ResultRenderer
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void Render(CallToolResult result)
    {
        if (result.IsError == true)
        {
            AnsiConsole.MarkupLine("[red]The tool reported an error:[/]");
        }

        var json = ExtractJson(result);
        if (json is null)
        {
            AnsiConsole.MarkupLine("[grey](no content returned)[/]");
            return;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            AnsiConsole.WriteLine(json);
            return;
        }

        RenderNode(node);
    }

    private static string? ExtractJson(CallToolResult result)
    {
        if (result.StructuredContent is { } structured)
        {
            return structured.GetRawText();
        }

        var text = string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>().Select(b => b.Text));

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void RenderNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                RenderArray(array);
                break;

            case JsonObject obj when obj["Rows"] is JsonArray rows:
                RenderResultEnvelope(obj, rows);
                break;

            case JsonObject obj when obj["Error"] is JsonValue error:
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.ToString())}[/]");
                break;

            default:
                PrintJson(node);
                break;
        }
    }

    private static void RenderResultEnvelope(JsonObject obj, JsonArray rows)
    {
        foreach (var property in obj)
        {
            if (property.Key is "Rows")
            {
                continue;
            }

            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(property.Key)}:[/] {Markup.Escape(property.Value?.ToJsonString() ?? "null")}");
        }

        RenderArray(rows);
    }

    private static void RenderArray(JsonArray array)
    {
        if (array.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey](no rows)[/]");
            return;
        }

        var objects = array.OfType<JsonObject>().ToList();
        if (objects.Count != array.Count)
        {
            // Mixed / scalar array — fall back to raw JSON.
            PrintJson(array);
            return;
        }

        var columns = objects
            .SelectMany(o => o.Select(p => p.Key))
            .Distinct()
            .ToList();

        var table = new Table().Border(TableBorder.Rounded).Expand();
        foreach (var column in columns)
        {
            table.AddColumn($"[bold]{Markup.Escape(column)}[/]");
        }

        foreach (var obj in objects)
        {
            var cells = columns
                .Select(c => Markup.Escape(FormatCell(obj[c])))
                .ToArray();
            table.AddRow(cells);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{array.Count} row(s).[/]");
    }

    private static string FormatCell(JsonNode? value) => value switch
    {
        null => "NULL",
        JsonValue v => v.ToString(),
        _ => value.ToJsonString(),
    };

    private static void PrintJson(JsonNode? node)
    {
        var json = node?.ToJsonString(Indented) ?? "null";
        AnsiConsole.Write(new Panel(new Text(json))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("JSON"),
        });
    }
}
