using System.Text.Json;

namespace SqlServerMcp.TestClient.Configuration;

/// <summary>
/// Builds <see cref="ClientOptions"/> from (in increasing precedence) appsettings.json,
/// the SQLSERVERMCP_CONNECTIONSTRING environment variable, and command-line arguments.
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ClientOptions Load(string[] args)
    {
        var options = LoadFromFile() ?? new ClientOptions();

        ApplyEnvironment(options);
        ApplyArguments(options, args);
        EnsureStdioArguments(options);

        return options;
    }

    private static ClientOptions? LoadFromFile()
    {
        // appsettings.json is copied next to the executable.
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ClientOptions>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static void ApplyEnvironment(ClientOptions options)
    {
        var envConnection = Environment.GetEnvironmentVariable("SQLSERVERMCP_CONNECTIONSTRING");
        if (!string.IsNullOrWhiteSpace(envConnection))
        {
            options.Stdio.ConnectionString = envConnection;
        }
    }

    private static void ApplyArguments(ClientOptions options, string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--stdio":
                    options.Transport = "stdio";
                    break;

                case "--http":
                    options.Transport = "http";
                    if (HasValue(args, i))
                    {
                        options.Http.Url = args[++i];
                    }
                    break;

                case "--url":
                    options.Transport = "http";
                    if (HasValue(args, i))
                    {
                        options.Http.Url = args[++i];
                    }
                    break;

                case "--server":
                    // Point stdio at a published server executable.
                    if (HasValue(args, i))
                    {
                        options.Transport = "stdio";
                        options.Stdio.Command = args[++i];
                        options.Stdio.Arguments = new List<string> { "--stdio" };
                    }
                    break;

                case "--project":
                    // Launch the server via `dotnet run --project <path> -- --stdio`.
                    if (HasValue(args, i))
                    {
                        options.Transport = "stdio";
                        options.Stdio.Command = "dotnet";
                        options.Stdio.Arguments = new List<string>
                        {
                            "run", "--project", args[++i], "--", "--stdio",
                        };
                    }
                    break;

                case "--connection":
                case "--connection-string":
                    if (HasValue(args, i))
                    {
                        options.Stdio.ConnectionString = args[++i];
                    }
                    break;
            }
        }
    }

    private static void EnsureStdioArguments(ClientOptions options)
    {
        if (options.UseHttp)
        {
            return;
        }

        if (options.Stdio.Arguments.Count == 0)
        {
            // Sensible default: run the server project in-place via the SDK.
            options.Stdio.Arguments = new List<string>
            {
                "run", "--project", "../SqlServerMcp/src/SqlServerMcp.Server", "--", "--stdio",
            };
        }
    }

    private static bool HasValue(string[] args, int index) =>
        index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal);
}
