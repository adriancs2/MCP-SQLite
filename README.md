<h1 align="center">MCP SQLite</h1>

<p align="center">
  <b>Let your AI agent create, inspect, query and change SQLite databases.</b><br>
  Open source freeware for Windows · Model Context Protocol (MCP) server · System.Data.SQLite
</p>

---

MCP SQLite gives an AI agent (Claude, or any other MCP client) direct access to
SQLite database files on your computer. The agent can find databases in a folder,
read their schema, run queries, and make changes, using the same four tools every
time.

It is a single small Windows program: no Node.js, no Python, no SQLite install.
The SQLite engine ships inside the download.

## Why use it

- **One tool for reads and writes.** `sqlite_query` runs a `SELECT` or an
  `UPDATE` alike. The agent doesn't have to pick a different tool for each.
- **Batches are all-or-nothing.** Send several statements together and they run
  in one transaction. If any of them fails, the whole batch is rolled back and the
  error names the statement that failed.
- **No stray files.** Querying a mistyped path fails with `DB_NOT_FOUND`
  instead of quietly creating an empty database. New databases are made only by
  `sqlite_create_database`.
- **Light on tokens.** Result sets are capped at 50 rows unless the agent asks for
  more, and come back as an ASCII table, JSON, CSV or Markdown.
- **Safe defaults.** Foreign keys are enforced on every connection, and a locked
  database is retried for five seconds before giving up.

## Requirements

- Windows 10 or Windows 11 (x64 or x86)
- .NET Framework 4.8, which is included with Windows 10 (1903 and later) and Windows 11

## Getting it

**Download** the latest `MCP-SQLite-v*.zip` from the
[Releases](https://github.com/adriancs2/MCP-SQLite/releases) page and unzip it to
a folder you own, for example `C:\Tools\MCP-SQLite\`. Keep the whole folder
together, because `MCP-SQLite.exe` needs the files and the `x64` / `x86` folders beside it.

**Or build it from source** with Visual Studio 2022 version 17.13 or later (the
solution uses the `.slnx` format), with the ".NET desktop development" workload:

1. Clone this repository.
2. Open `src/MCP-SQLite/MCP-SQLite.slnx`.
3. Restore NuGet packages if Visual Studio asks, choose the **Release**
   configuration, and build.

The program is produced in `src/MCP-SQLite/bin/Release/`.

## Connecting it to your AI client

MCP SQLite is a standard **stdio** MCP server: the client starts
`MCP-SQLite.exe` and talks to it over standard input and output. Point your client
at the full path of the executable.

**Claude Desktop** (including its Code tab): add this to `claude_desktop_config.json`, then restart
Claude Desktop. The easiest way to open the file is Settings → Developer → Edit Config. With the
current Windows installer it is at
`%USERPROFILE%\AppData\Local\Claude-Data\claude_pzs8sxrjxfjjc\claude_desktop_config.json`
(older installs: `%APPDATA%\Claude\claude_desktop_config.json`).

```json
{
  "mcpServers": {
    "mcp-sqlite": {
      "command": "C:\\Tools\\MCP-SQLite\\MCP-SQLite.exe"
    }
  }
}
```

**Claude Code**

```bash
claude mcp add mcp-sqlite -- "C:\Tools\MCP-SQLite\MCP-SQLite.exe"
```

**Google Gemini Antigravity**: add this to `%USERPROFILE%\.gemini\config\mcp_config.json`
(create the file if it does not exist), then restart Antigravity:

```json
{
  "mcpServers": {
    "mcp-sqlite": {
      "command": "C:\\Tools\\MCP-SQLite\\MCP-SQLite.exe",
      "args": []
    }
  }
}
```

If the file already lists other servers, add the `"mcp-sqlite"` entry inside the
existing `"mcpServers"` object rather than adding a second one.

**Any other MCP client**: register a stdio server whose command is the path to
`MCP-SQLite.exe`. It takes no arguments and needs no environment variables.

Then just ask: *"Find the SQLite databases under D:\Projects and show me the schema of the biggest one."*

## Tools

Four tools, all prefixed `sqlite_`. Each one takes the full path of a database file
or folder.

| Tool | What it does |
|---|---|
| `sqlite_list_databases` | Finds database files under a folder, recursively by default. Matches `.db` `.sqlite` `.sqlite3` `.db3`, or the extensions you pass. Lists path, size and last-modified time. |
| `sqlite_schema` | Shows columns, indexes and the `CREATE` statement for every table, or only the tables you name. |
| `sqlite_query` | Runs SQL, both reads and writes. Pass one statement as `sql`, or several as `statements` to run them in a single transaction. `SELECT` and `PRAGMA` return rows; everything else reports the rows affected. |
| `sqlite_create_database` | Creates a new, empty database file and any missing parent folders. It won't replace an existing file unless `overwrite` is `true`. |

### `sqlite_query` options

| Parameter | Default | Meaning |
|---|---|---|
| `database` | (required) | Full path to the database file. |
| `sql` | | One SQL string. If it holds several `;`-separated statements, every result set is returned. |
| `statements` | | An array of SQL statements, run in one transaction. |
| `max_rows` | `50` | Maximum rows returned per result set (up to 100 000). |
| `format` | `ascii` | `ascii`, `json`, `csv` or `markdown`. In `json`, numbers stay numbers and `NULL` is a real `null`. |
| `use_transaction` | `true` | Set `false` to run each statement on its own. Batches containing `VACUUM`, `ATTACH`, `DETACH` or `PRAGMA journal_mode=…` run without a transaction automatically, because SQLite doesn't allow them inside one. |

### Example

```text
sqlite_query  database="C:\data\shop.db"
              statements=["INSERT INTO customer(name) VALUES ('Ann')",
                          "INSERT INTO orders(customer_id, total) VALUES (last_insert_rowid(), 42.5)"]

─── Statement 1/2 ───
OK. 1 row(s) affected.

─── Statement 2/2 ───
OK. 1 row(s) affected.
```

```text
sqlite_query  database="C:\data\shop.db"  sql="SELECT id, name FROM customer"

+----+------+
| id | name |
+----+------+
| 1  | Ann  |
+----+------+

Total: 1 row(s).
```

Errors come back with a short code the agent can act on: `DB_NOT_FOUND`,
`DB_EXISTS`, `DIR_NOT_FOUND`, `INVALID_PARAMS` or `SQLITE_ERROR`.

## Configuration

On first run the program writes `mcp-config.json` next to the executable. Every
setting is optional. Restart the MCP server after changing the file.

| Setting | Default | Meaning |
|---|---|---|
| `default_max_rows` | `50` | Rows returned per result set when the agent does not pass `max_rows`. |
| `busy_timeout_ms` | `5000` | How long to wait for a database that another program has locked. |
| `command_timeout_seconds` | `120` | How long a single statement may run. `0` means no limit. |
| `debug_logging` | `false` | Write every request and response to `mcp_debug.log` next to the executable. |

The text the server sends to the AI as usage guidance lives in
`system-prompts.txt`, also next to the executable. Edit it to suit your own
workflow.

## Privacy and safety

- MCP SQLite runs entirely on your computer. It contains no telemetry and makes
  no network connections.
- There is no sandbox. The agent can open, change or overwrite any database file
  your Windows account can, including deleting data with `DELETE` or `DROP`. Keep
  backups of databases that matter, and review what the agent does to them.

## Testing

`tests/run-tests.ps1` starts the built executable and drives it over stdio the
way an MCP client would. It runs 47 checks covering the protocol, all four tools,
every output format, transactions and rollback, and the error codes:

```bash
powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 -Exe src\MCP-SQLite\bin\Release\MCP-SQLite.exe
```

## Troubleshooting

**`Unable to load DLL 'SQLite.Interop.dll'`.**
The `x64` and `x86` folders must stay next to `MCP-SQLite.exe`. Unzip the whole
release, not only the executable.

**`database is locked`.**
Another program has the database open for writing. Close it, or raise
`busy_timeout_ms`.

**Nothing is written to `mcp-config.json`.**
The program cannot write to its own folder (for example under `Program Files`).
It runs on default settings. Move it to a folder you own to customize it.

## Project layout

```
src/MCP-SQLite/
  Program.cs          Entry point: starts the MCP server on stdin/stdout
  Core/               JSON-RPC and MCP protocol, tool discovery, configuration
  Services/           SqliteService: connections, transactions, schema, formatting
  Tools/              One class per MCP tool
  system-prompts.txt  Usage guidance sent to the AI on connect
tests/
  run-tests.ps1       End-to-end tests against the built executable
```

Adding a tool means adding one class that implements `ITool`. It is
discovered automatically.

## License

MCP SQLite is free software released into the public domain under
[The Unlicense](LICENSE). Use it, change it and share it however you like.

The bundled [System.Data.SQLite](https://system.data.sqlite.org/) and SQLite are
in the public domain; [Newtonsoft.Json](https://www.newtonsoft.com/json) is MIT licensed.

Project home: <https://github.com/adriancs2/MCP-SQLite>
