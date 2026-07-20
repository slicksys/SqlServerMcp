# SqlServerMcp

An MCP (Model Context Protocol) server that gives coding/LOB-design agents fast, read-only
access to a SQL Server database's `INFORMATION_SCHEMA` / `sys.*` metadata — schemas, tables,
columns, keys, indexes, relationships, extended-property descriptions — plus small data
previews and safe ad-hoc `SELECT` queries. Built for exploring an existing database to decide
table structures for a line-of-business app.

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

## Configuration

The connection string is **not** committed to source. Provide it via either:

- Environment variable `SQLSERVERMCP_CONNECTIONSTRING` (takes precedence), or
- `SqlServerMcp:ConnectionString` in `appsettings.json` / `appsettings.Development.json` / user secrets.

```bash
setx SQLSERVERMCP_CONNECTIONSTRING "Server=myserver;Database=mydb;User Id=myuser;Password=mypassword;TrustServerCertificate=true"
```

Other tunables (all optional, under the `SqlServerMcp` config section):

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

## Running

The server supports two transports from the same binary:

### stdio (for IDE/agent integrations — Claude Desktop, Cursor, VS Code, etc.)

```bash
dotnet run --project src/SqlServerMcp.Server -- --stdio
```

Example MCP client config (e.g. `claude_desktop_config.json` or VS Code `mcp.json`):

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/Projects/SqlServerMcp/src/SqlServerMcp.Server", "--", "--stdio"],
      "env": {
        "SQLSERVERMCP_CONNECTIONSTRING": "Server=myserver;Database=mydb;User Id=myuser;Password=mypassword;TrustServerCertificate=true"
      }
    }
  }
}
```

For a published build, point `command` at the built executable instead of `dotnet run`:

```bash
dotnet publish src/SqlServerMcp.Server -c Release -o publish
```

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "D:/Projects/SqlServerMcp/publish/SqlServerMcp.Server.exe",
      "args": ["--stdio"],
      "env": { "SQLSERVERMCP_CONNECTIONSTRING": "..." }
    }
  }
}
```

### HTTP / SSE (for remote or multi-client use)

```bash
dotnet run --project src/SqlServerMcp.Server --urls http://localhost:5199
```

MCP endpoint: `http://localhost:5199/mcp` (Streamable HTTP transport, which also
supports SSE-style streaming responses). Point any MCP HTTP client at that URL.

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
