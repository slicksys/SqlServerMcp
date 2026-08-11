# SqlServerMcp

  An MCP (Model Context Protocol) server that gives coding/LOB-design agents fast, read-only
access to a SQL Server database's `INFORMATION_SCHEMA` / `sys.*` metadata — schemas, tables,
columns, keys, indexes, relationships, extended-property descriptions — plus small data
previews and safe ad-hoc `SELECT` queries. Built for exploring and analysing existing database
for ETL or migration purposes, or for generating table structures for feature additions.

## Tools

| Tool | Purpose |
|---|---|
| `list_schemas` | List database schemas (skips `sys`/system schemas by default). |
| `list_tables` | List tables/views per schema, with approx. row counts and descriptions. |
| `get_table_columns` | Column details: type, length/precision, nullable, default, identity, PK flag, description. |
| `get_table_relationships` | Foreign keys, both outgoing and incoming, for a table. |
| `get_table_indexes` | Indexes (incl. PK/unique), key column order, included columns. |
| `search_schema` | Substring search across table names, column names, and `MS_Description` extended properties. |
| `preview_table_data` | `TOP N` row sample from a table/view. |
| `execute_readonly_query` | Ad-hoc `SELECT` (or `WITH ... SELECT`) query; anything else is rejected. |

## Usage and parameters

### Smoke-test script

The repository includes a PowerShell smoke test that builds the server, starts it over HTTP, exercises the registered MCP tools, and then shuts it down.

```pwsh
./test-mcp.ps1
```

Supported parameters:

| Parameter | Default | Description |
|---|---:|---|
| `-Port` | `5210` | Local HTTP port for the temporary server instance. |
| `-ProjectPath` | `src/SqlServerMcp.Server/SqlServerMcp.Server.csproj` | Path to the server project file. |
| `-SkipBuild` | `false` | Reuses existing build output instead of running `dotnet build`. |

Example:

```pwsh
./test-mcp.ps1 -Port 5300 -SkipBuild
```

### Server startup parameters

The server accepts a single transport switch:

| Parameter | Meaning |
|---|---|
| `--stdio` | Runs the MCP server over stdio for IDE/agent integrations. |
| *(no `--stdio`)* | Runs the HTTP transport and exposes the MCP endpoint at `/mcp`. |

Additional ASP.NET Core host parameters are also supported, such as `--urls` for the HTTP listener:

```pwsh
dotnet run --project src/SqlServerMcp.Server -- --stdio
dotnet run --project src/SqlServerMcp.Server --urls http://localhost:5199
```

## Configuration

### How the connection string is resolved

At startup the server resolves the effective connection string in the following
priority order (first match wins):

1. **`SQLSERVERMCP_CONNECTIONSTRING`** environment variable — highest precedence.
2. **`SqlServerMcp:ConnectionString`** — a literal connection string in
   `appsettings.json` / `appsettings.Development.json` / user secrets.
3. **`ConnectionStrings:{name}`** — a *named* entry under the standard
   `ConnectionStrings` section, selected by **`SqlServerMcp:ConnectionStringName`**.

This project uses option **3** by default: `appsettings.json` defines several named
connections and picks one via `ConnectionStringName`.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=(localdb)\\ProjectModels;Initial Catalog=impromed;Integrated Security=True;Encrypt=True;Trust Server Certificate=True",
    "ImproConnection":   "Data Source=SERVER\\INSTANCE;Initial Catalog=SSDCCRM;Integrated Security=True;Encrypt=False;Trust Server Certificate=True",
    "PetOneTwoConnection": "Data Source=SERVER\\INSTANCE;Initial Catalog=ssdcdata;Integrated Security=True;Encrypt=False;Trust Server Certificate=True"
  },
  "SqlServerMcp": {
    "ConnectionStringName": "DefaultConnection"
  }
}
```

### Choosing which named connection to use

Switch connections **without editing files** by overriding the selector at launch:

```pwsh
# Select a different named entry (note the double underscore for nested keys)
$env:SqlServerMcp__ConnectionStringName = "PetOneTwoConnection"

# ...or bypass names entirely with a literal string (highest precedence)
$env:SQLSERVERMCP_CONNECTIONSTRING = "Server=myserver;Database=mydb;User Id=myuser;Password=***;TrustServerCertificate=true"
```

The same keys can be passed as command-line arguments (use `:` on the CLI):

```pwsh
dotnet run --project src/SqlServerMcp.Server -- --stdio --SqlServerMcp:ConnectionStringName=ImproConnection
```

### Keeping secrets out of source control

**Do not commit SQL logins/passwords to `appsettings.json`.** Prefer Integrated
Security, or store the connection string in user secrets (the project already
declares `UserSecretsId=sqlservermcp-server`):

```pwsh
# A named entry...
dotnet user-secrets --project src/SqlServerMcp.Server set "ConnectionStrings:DefaultConnection" "Server=...;Database=...;Integrated Security=True;Encrypt=True"

# ...or the literal override
dotnet user-secrets --project src/SqlServerMcp.Server set "SqlServerMcp:ConnectionString" "Server=...;Database=...;"
```

> User secrets are only loaded automatically in the **Development** environment.
> For production, use the `SQLSERVERMCP_CONNECTIONSTRING` environment variable or a
> secret store.

### Other tunables

All optional, under the `SqlServerMcp` config section:

| Setting | Default | Meaning |
|---|---|---|
| `DefaultPreviewRows` | 10 | Default rows for `preview_table_data`. |
| `MaxPreviewRows` | 200 | Hard cap for `preview_table_data`. |
| `MaxQueryRows` | 500 | Hard cap for `execute_readonly_query`. |
| `CommandTimeoutSeconds` | 30 | SQL command timeout. |

**Security recommendation:** use a SQL login with only `db_datareader` (+ `VIEW DEFINITION`)
permissions on the target database. `execute_readonly_query` statically rejects non-SELECT
statements and runs inside a rolled-back transaction as a safety net, but least-privilege at
the database level is the real security boundary.

### Configuration precedence summary

The effective connection string is resolved in this order:

1. `SQLSERVERMCP_CONNECTIONSTRING`
2. `SqlServerMcp:ConnectionString`
3. `ConnectionStrings:{ConnectionStringName}` via `SqlServerMcp:ConnectionStringName`

The same values can be supplied with environment variables or CLI arguments. For example:

```pwsh
$env:SqlServerMcp__ConnectionStringName = "DefaultConnection"
$env:SQLSERVERMCP_CONNECTIONSTRING = "Server=myserver;Database=mydb;Integrated Security=True;TrustServerCertificate=True"
```

Or from the command line:

```pwsh
dotnet run --project src/SqlServerMcp.Server -- --stdio --SqlServerMcp:ConnectionStringName=ImproConnection
```

## Running

The server supports two transports from the same binary. If no connection string is
resolved (see [Configuration](#configuration)), startup fails with a clear error.

### 1. From Visual Studio

Open `SqlServerMcp.slnx`, set `SqlServerMcp.Server` as the startup project, and run
(F5 / Ctrl+F5). By default this launches the **HTTP** transport. To debug the stdio
transport instead, add `--stdio` to the launch profile's command-line arguments
(Project Properties → Debug → *Command line arguments*).

### 2. stdio (for IDE/agent integrations — Claude Desktop, Cursor, VS Code, etc.)

```pwsh
dotnet run --project src/SqlServerMcp.Server -- --stdio
```

Example MCP client config (e.g. `claude_desktop_config.json` or VS Code `mcp.json`).
Because stdio clients launch the process, they can inject configuration via `env`:

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/Projects/SqlServerMcp/src/SqlServerMcp.Server", "--", "--stdio"],
      "env": {
        "SqlServerMcp__ConnectionStringName": "DefaultConnection"
      }
    }
  }
}
```

Or supply a literal connection string via `env` instead of selecting a name:

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/Projects/SqlServerMcp/src/SqlServerMcp.Server", "--", "--stdio"],
      "env": {
        "SQLSERVERMCP_CONNECTIONSTRING": "Server=myserver;Database=mydb;User Id=myuser;Password=***;TrustServerCertificate=true"
      }
    }
  }
}
```

### 3. HTTP / SSE (for remote or multi-client use)

```pwsh
dotnet run --project src/SqlServerMcp.Server --urls http://localhost:5199
```

MCP endpoint: `http://localhost:5199/mcp` (Streamable HTTP transport, which also
supports SSE-style streaming responses). Point any MCP HTTP client at that URL.

> Note: with the HTTP transport a single process is shared by all clients, so the
> connection string is fixed at startup and applies to every caller.

### 4. Published / self-contained build

Framework-dependent publish, then point `command` at the built executable instead of
`dotnet run`:

```pwsh
dotnet publish src/SqlServerMcp.Server -c Release -o publish
```

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "D:/Projects/SqlServerMcp/publish/SqlServerMcp.Server.exe",
      "args": ["--stdio"],
      "env": { "SqlServerMcp__ConnectionStringName": "DefaultConnection" }
    }
  }
}
```

To produce a single self-contained executable (no .NET runtime required on the target
machine), uncomment the self-contained properties in
`src/SqlServerMcp.Server/SqlServerMcp.Server.csproj` and publish with a runtime
identifier:

```pwsh
dotnet publish src/SqlServerMcp.Server -c Release -r win-x64 --self-contained -o publish
```

The same executable runs the HTTP transport when launched without `--stdio`:

```pwsh
./publish/SqlServerMcp.Server.exe --urls http://localhost:5199
```

## Project layout

```
src/SqlServerMcp.Server/
  Configuration/SqlServerMcpOptions.cs   - options bound from config/env
  Data/SqlConnectionFactory.cs           - opens SqlConnection from the configured connection string
  Data/ReadOnlyQueryGuard.cs             - static validation for ad-hoc SELECT-only queries
  Data/SqlRowReader.cs                   - reads SqlDataReader rows into JSON-friendly dictionaries
  Tools/SchemaExplorerTools.cs           - list_schemas, list_tables, get_table_columns,
                                            get_table_relationships, get_table_indexes, search_schema
  Tools/DataAccessTools.cs               - preview_table_data, execute_readonly_query
  Program.cs                             - wires up stdio or HTTP transport based on --stdio flag
```
