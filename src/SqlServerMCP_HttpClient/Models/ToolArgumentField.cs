using System.Text.Json;

namespace SqlServerMcp.WebClient.Models;

/// <summary>
/// A single tool-input field derived from the tool's JSON schema, plus the raw text the
/// user has typed into the corresponding form control.
/// </summary>
public sealed class ToolArgumentField
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public string? Description { get; init; }

    public bool Required { get; init; }

    public string RawValue { get; set; } = string.Empty;

    /// <summary>Converts the field's schema type into the closest matching HTML input type.</summary>
    public string InputType => Type switch
    {
        "integer" or "number" => "number",
        "boolean" => "checkbox",
        _ => "text",
    };

    /// <summary>Builds the argument list (name -&gt; typed value) that should be sent to the tool.</summary>
    public static Dictionary<string, object?> BuildArguments(IEnumerable<ToolArgumentField> fields)
    {
        var arguments = new Dictionary<string, object?>();
        foreach (var field in fields)
        {
            if (field.InputType == "checkbox")
            {
                // Checkboxes always have a definite value (true/false), so always include it.
                arguments[field.Name] = string.Equals(field.RawValue, "true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (string.IsNullOrWhiteSpace(field.RawValue))
            {
                continue;
            }

            arguments[field.Name] = Convert(field.RawValue.Trim(), field.Type);
        }

        return arguments;
    }

    private static object Convert(string input, string type) => type switch
    {
        "integer" => long.Parse(input),
        "number" => double.Parse(input),
        "boolean" => string.Equals(input, "true", StringComparison.OrdinalIgnoreCase),
        _ => input,
    };

    /// <summary>Builds the ordered list of input fields for a tool from its JSON input schema.</summary>
    public static List<ToolArgumentField> FromSchema(JsonElement schema)
    {
        var fields = new List<ToolArgumentField>();

        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return fields;
        }

        var required = ReadRequired(schema);

        foreach (var property in properties.EnumerateObject())
        {
            var propSchema = property.Value;
            var description = propSchema.TryGetProperty("description", out var d) ? d.GetString() : null;

            fields.Add(new ToolArgumentField
            {
                Name = property.Name,
                Type = ReadType(propSchema),
                Description = description,
                Required = required.Contains(property.Name),
            });
        }

        return fields;
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
}
