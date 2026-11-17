param([string]$Exe)
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP ("mcpsqlite-test-" + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory $work | Out-Null
$db = Join-Path $work 'sub\test.db'

$psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$proc = [Diagnostics.Process]::Start($psi)
$stdin = New-Object IO.StreamWriter($proc.StandardInput.BaseStream, (New-Object Text.UTF8Encoding($false)))
$stdin.AutoFlush = $true
$script:id = 0; $script:pass = 0; $script:fail = 0

function Rpc($method, $params) {
  $script:id++
  $msg = @{ jsonrpc = '2.0'; id = $script:id; method = $method }
  if ($params) { $msg.params = $params }
  $stdin.WriteLine(($msg | ConvertTo-Json -Depth 10 -Compress))
  return ($proc.StandardOutput.ReadLine() | ConvertFrom-Json)
}
function Call($name, $arguments) {
  $r = Rpc 'tools/call' @{ name = $name; arguments = $arguments }
  $text = ($r.result.content | ForEach-Object { $_.text }) -join "`n"
  return [pscustomobject]@{ IsError = [bool]$r.result.isError; Text = $text; Raw = $r }
}
function Check($label, $cond, $detail) {
  if ($cond) { $script:pass++; Write-Output "PASS  $label" }
  else { $script:fail++; Write-Output "FAIL  $label`n      $detail" }
}

# ---- protocol ----
$r = Rpc 'tools/list' @{}
Check 'tools/list before initialize is rejected' ($r.error.code -eq -32600) ($r | ConvertTo-Json -Compress)
$r = Rpc 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'test'; version = '1' } }
Check 'initialize echoes protocol version' ($r.result.protocolVersion -eq '2025-06-18') ''
Check 'initialize server name' ($r.result.serverInfo.name -eq 'MCP SQLite') $r.result.serverInfo.name
Check 'initialize includes instructions' ($r.result.instructions -match 'sqlite_query') ''
$r = Rpc 'initialize' @{ protocolVersion = '1999-01-01' }
Check 'unknown protocol version falls back to newest' ($r.result.protocolVersion -eq '2025-11-25') $r.result.protocolVersion
$stdin.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')   # no reply expected
$r = Rpc 'ping' @{}
Check 'ping (and notification got no reply)' ($r.id -eq $script:id -and $r.result -ne $null) ($r | ConvertTo-Json -Compress)
$r = Rpc 'tools/list' @{}
$names = ($r.result.tools | ForEach-Object name) -join ','
Check 'tools/list returns 4 sqlite tools' ($names -eq 'sqlite_create_database,sqlite_list_databases,sqlite_query,sqlite_schema') $names
$q = $r.result.tools | Where-Object name -eq 'sqlite_query'
Check 'sqlite_query schema requires database' ($q.inputSchema.required -contains 'database') ''
Check 'sqlite_query has use_transaction param' ($q.inputSchema.properties.use_transaction.type -eq 'boolean') ''
$r = Rpc 'bogus/method' @{}
Check 'unknown method -> -32601' ($r.error.code -eq -32601) ''
$r = Rpc 'tools/call' @{ name = 'nope'; arguments = @{} }
Check 'unknown tool -> -32602' ($r.error.code -eq -32602) ''
$stdin.WriteLine('{not json')
$r = $proc.StandardOutput.ReadLine() | ConvertFrom-Json
Check 'malformed JSON -> -32700' ($r.error.code -eq -32700) ''

# ---- sqlite_create_database ----
$c = Call 'sqlite_create_database' @{ database = $db }
Check 'create database (with new parent dir)' (-not $c.IsError -and (Test-Path $db)) $c.Text
$c = Call 'sqlite_create_database' @{ database = $db }
Check 'create refuses existing file' ($c.IsError -and $c.Text -match 'DB_EXISTS') $c.Text
$c = Call 'sqlite_create_database' @{}
Check 'create without path -> INVALID_PARAMS' ($c.IsError -and $c.Text -match 'INVALID_PARAMS') $c.Text

# ---- sqlite_query: writes ----
$c = Call 'sqlite_query' @{ database = $db; statements = @(
  'CREATE TABLE author (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE)',
  'CREATE TABLE book (id INTEGER PRIMARY KEY, author_id INTEGER NOT NULL REFERENCES author(id), title TEXT, price REAL, cover BLOB)',
  'CREATE INDEX ix_book_author ON book(author_id)',
  "INSERT INTO author(name) VALUES ('Ann'),('Bob'),('Cy')",
  "INSERT INTO book(author_id,title,price,cover) VALUES (1,'Alpha',9.5,x'0102'),(1,'Beta, the ""sequel""',NULL,NULL),(2,'Gamma|pipe',12,NULL)") }
Check 'batch DDL + inserts' (-not $c.IsError -and $c.Text -match 'Statement 5/5' -and $c.Text -match '3 row\(s\) affected') $c.Text

$c = Call 'sqlite_query' @{ database = $db; sql = "UPDATE book SET price = 10 WHERE id = 1" }
Check 'single UPDATE reports affected rows' ($c.Text -match 'OK\. 1 row\(s\) affected') $c.Text

# ---- sqlite_query: reads / formats ----
$c = Call 'sqlite_query' @{ database = $db; sql = 'SELECT id, title, price, cover FROM book ORDER BY id' }
Check 'ascii SELECT' ($c.Text -match '\| 1  \| Alpha' -and $c.Text -match 'NULL' -and $c.Text -match '\[BLOB: 2 bytes\]' -and $c.Text -match 'Total: 3 row') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'SELECT id, title, price FROM book ORDER BY id'; format = 'json' }
$j = ($c.Text -split 'Total:')[0] | ConvertFrom-Json
Check 'json SELECT is typed (numbers, null)' ($j.Count -eq 3 -and $j[0].price -eq 10 -and $j[1].price -eq $null -and $j[0].id -isnot [string]) $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'SELECT title FROM book ORDER BY id'; format = 'csv' }
Check 'csv escapes quotes and commas' ($c.Text -match '"Beta, the ""sequel"""') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'SELECT title FROM book ORDER BY id'; format = 'markdown' }
Check 'markdown escapes pipes' ($c.Text -match 'Gamma\\\|pipe' -and $c.Text -match '\| -') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'SELECT * FROM book'; max_rows = 2 }
Check 'max_rows truncates' ($c.Text -match 'truncated at 2 rows' -and $c.Text -match 'Total: 2 row') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'SELECT 1 AS a; SELECT 2 AS b' }
Check 'multiple result sets in one sql string' ($c.Text -match '\| a' -and $c.Text -match '\| b') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'PRAGMA foreign_keys' }
Check 'foreign keys are ON' ($c.Text -match '\| 1 ') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = "SELECT 'héllo 中文 ✓' AS s" }
Check 'UTF-8 round trip' ($c.Text -match 'héllo 中文 ✓') $c.Text

# ---- sqlite_query: atomicity & errors ----
$c = Call 'sqlite_query' @{ database = $db; statements = @("INSERT INTO author(name) VALUES ('Dee')", "INSERT INTO author(name) VALUES ('Ann')") }
Check 'failing batch reports statement 2' ($c.IsError -and $c.Text -match 'Statement 2 of 2 failed' -and $c.Text -match 'UNIQUE') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = "SELECT COUNT(*) AS n FROM author WHERE name='Dee'" }
Check 'failed batch rolled back' ($c.Text -match '\| 0 ') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'INSERT INTO book(author_id,title) VALUES (99,''x'')' }
Check 'FK violation rejected' ($c.IsError -and $c.Text -match 'FOREIGN KEY') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'SELEC nonsense' }
Check 'syntax error -> SQLITE_ERROR' ($c.IsError -and $c.Text -match 'SQLITE_ERROR') $c.Text
$c = Call 'sqlite_query' @{ database = (Join-Path $work 'missing.db'); sql = 'SELECT 1' }
Check 'missing db -> DB_NOT_FOUND, no file created' ($c.IsError -and $c.Text -match 'DB_NOT_FOUND' -and -not (Test-Path (Join-Path $work 'missing.db'))) $c.Text
$c = Call 'sqlite_query' @{ database = $db }
Check 'no sql -> INVALID_PARAMS' ($c.IsError -and $c.Text -match 'INVALID_PARAMS') $c.Text

# ---- no-transaction statements ----
$c = Call 'sqlite_query' @{ database = $db; sql = 'VACUUM' }
Check 'VACUUM runs (auto no-transaction)' (-not $c.IsError) $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = 'PRAGMA journal_mode=WAL' }
Check 'journal_mode=WAL runs' (-not $c.IsError -and $c.Text -match 'wal') $c.Text
$db2 = Join-Path $work 'other.db'
Call 'sqlite_create_database' @{ database = $db2 } | Out-Null
$c = Call 'sqlite_query' @{ database = $db; statements = @("ATTACH DATABASE '$db2' AS o", 'CREATE TABLE o.t(x)', 'INSERT INTO o.t VALUES (42)', 'SELECT x FROM o.t', 'DETACH DATABASE o') }
Check 'ATTACH/DETACH batch' (-not $c.IsError -and $c.Text -match '42') $c.Text
$c = Call 'sqlite_query' @{ database = $db; statements = @("INSERT INTO author(name) VALUES ('Eve')", "INSERT INTO author(name) VALUES ('Ann')"); use_transaction = $false }
Check 'use_transaction=false keeps earlier statements' ($c.IsError -and $c.Text -match 'were committed') $c.Text
$c = Call 'sqlite_query' @{ database = $db; sql = "SELECT COUNT(*) FROM author WHERE name='Eve'" }
Check '... Eve was committed' ($c.Text -match '\| 1 ') $c.Text

# ---- sqlite_schema ----
$c = Call 'sqlite_schema' @{ database = $db }
Check 'schema all tables' ($c.Text -match 'Table 1/2: author' -and $c.Text -match 'Table 2/2: book' -and $c.Text -match 'ix_book_author' -and $c.Text -match 'CREATE TABLE book') $c.Text
$c = Call 'sqlite_schema' @{ database = $db; tables = @('book'); format = 'markdown' }
Check 'schema one table, markdown' ($c.Text -match 'Total: 1 table' -and $c.Text -match '\| cid') $c.Text
$c = Call 'sqlite_schema' @{ database = $db; tables = @('nope') }
Check 'schema unknown table' ($c.Text -match 'table not found') $c.Text
$c = Call 'sqlite_schema' @{ database = $db2 }
Check 'schema on other db' ($c.Text -match 'Table 1/1: t') $c.Text

# ---- sqlite_list_databases ----
Set-Content (Join-Path $work 'sub\notes.sqlite3') 'x'
Set-Content (Join-Path $work 'ignore.txt') 'x'
$c = Call 'sqlite_list_databases' @{ directory = $work }
Check 'list recursive' ($c.Text -match 'test\.db' -and $c.Text -match 'other\.db' -and $c.Text -match 'notes\.sqlite3' -and $c.Text -notmatch 'ignore\.txt') $c.Text
$c = Call 'sqlite_list_databases' @{ directory = $work; recursive = $false }
Check 'list non-recursive' ($c.Text -match 'other\.db' -and $c.Text -notmatch 'test\.db') $c.Text
$c = Call 'sqlite_list_databases' @{ directory = $work; extensions = @('txt') }
Check 'list custom extension without dot' ($c.Text -match 'ignore\.txt' -and $c.Text -match 'Total: 1') $c.Text
$c = Call 'sqlite_list_databases' @{ directory = (Join-Path $work 'nowhere') }
Check 'list missing dir -> DIR_NOT_FOUND' ($c.IsError -and $c.Text -match 'DIR_NOT_FOUND') $c.Text

# ---- overwrite ----
$c = Call 'sqlite_create_database' @{ database = $db; overwrite = $true }
Check 'overwrite recreates db' (-not $c.IsError) $c.Text
$c = Call 'sqlite_schema' @{ database = $db }
Check 'overwritten db is empty (WAL not replayed)' ($c.Text -match 'No tables found') $c.Text

# ---- shutdown ----
$stdin.Close()
$exited = $proc.WaitForExit(5000)
Check 'process exits when stdin closes' ($exited -and $proc.ExitCode -eq 0) "exit=$exited"

Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
Write-Output ""
Write-Output "RESULT: $script:pass passed, $script:fail failed"
if ($script:fail -gt 0) { exit 1 }
