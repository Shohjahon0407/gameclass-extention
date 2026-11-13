# Manajoy GameClass Inspector (GCInspector v0.1)

Phase 1 of the Manajoy ↔ GameClass connector. This is a **read-only** console tool. It runs on a gaming club's operator PC and finds out where and how the local GameClass3 installation stores its data. It produces:

- a readable report in the console window
- a JSON report for the engineering team

It does **not** talk to Manajoy, and it does not book, lock or change anything.

## 1. Purpose

GameClass3 keeps PCs, sessions, tariffs, clients and reservations in a Microsoft SQL Server database. On most installations this is the bundled **MSDE 2000**, which is SQL Server 2000. Before we write the production connector we need facts from a real installation. The inspector collects them:

1. The environment: OS, architecture, user, whether the process is elevated.
2. Local SQL Server / MSDE instances. It looks in Windows services, the registry (64- and 32-bit views), common local names and configured names.
3. Whether each instance accepts a connection, and which server version it runs.
4. The user databases on each instance, and whether each one can be opened.
5. How much each database looks like a gaming-club database. This is heuristic only: it scores table, view and column names in English, Russian and transliteration.
6. The structure of those databases: tables, views, columns, primary keys, foreign keys, approximate row counts and stored-procedure *names*.
7. Optionally, at most 5 masked rows from tables you name explicitly. This is off by default.

No schema is hard-coded. The "Computers-like table" in the report is a guess based on names and column types.

## 2. Safety / read-only guarantee

| Guarantee | How it is enforced |
|---|---|
| Only `SELECT` statements | All SQL lives in `Infrastructure/SqlQueries.cs`. `ReadOnlyQueryExecutor` is the only code that runs commands, and it passes every statement through `ReadOnlyQueryGuard` first. The guard rejects anything that is not a single `SELECT`, and anything containing `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`, `EXEC`, `INTO`, `SET`, `USE`, `DBCC`, `OPENROWSET`, `sp_`/`xp_`, and similar. Unit tests run every catalog query through the guard. |
| No transactions | None are ever opened. Connection pooling is off, so the server-side audit trail contains only our SELECTs. |
| No table scans or data dumps | Row counts come from index metadata (`sysindexes`), not `COUNT(*)`. Business rows are read only when `AllowSampleRows=true`, only from tables you list, at most 5 rows, with `NOLOCK` so the live GameClass is not blocked. |
| No personal data | When sampling, columns that look sensitive are **never selected**. That covers passwords, hashes, phones, e-mails, cards, addresses, names, birth dates and similar, in both Latin and Cyrillic. Every text column of client and operator tables is masked. E-mail- or phone-looking values in other columns are masked too. |
| No secrets in output | Connection strings exist only inside `SqlConnectionFactory`. Reports and logs contain server names, provider and authentication type only. |
| No system changes | Services are only queried for status and never started or stopped. The registry is opened read-only. There are no firewall, TCP/IP, TLS or SQL Server configuration changes, and no Windows event-log writes. |
| No credential guessing | Windows authentication is tried first. A SQL login is used only when you configure one, and there is one attempt per client library. |

If access is denied, the tool reports it with a hint and moves on. It never tries to work around the denial.

Verified end-to-end against SQL Server 2022 with an Extended Events audit filtered on the application name `Manajoy.GameClass.Inspector`:
- every statement the tool sent was a `SELECT`;
- table checksums and object modify dates were identical before and after a run.

## 3. Requirements

**To run (operator PC):**
- **Windows 10 1607+ / Windows 11 / Windows Server 2012+.** Use x64, or x86 for 32-bit Windows. .NET 8 does not run on Windows 7/8.1, so if the club PC runs one of those, tell the team: it needs a different build.
- No .NET installation needed; the published exe is self-contained.
- The Windows built-in **"SQL Server" ODBC driver** (`sqlsrv32.dll`). It ships with every Windows version and is used automatically for SQL Server 2000 / MSDE.

**To build:**
- .NET 8 SDK. Windows, Linux or macOS; the project sets `EnableWindowsTargeting`.

## 4. Build

```bash
dotnet restore
dotnet build
dotnet test
```

Publish a Windows executable. This gives one `.exe` plus an editable `appsettings.json`:

```bash
dotnet publish \
  src/Manajoy.GameClass.Inspector \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -o publish/win-x64
```

For 32-bit Windows use `-r win-x86 -o publish/win-x86`.

## 5. Run

```bash
dotnet run --project src/Manajoy.GameClass.Inspector
```

- With `dotnet run`, the `output/` and `logs/` folders are created next to the built assembly, in `src/Manajoy.GameClass.Inspector/bin/<Configuration>/net8.0-windows/`.
- With the published exe, they are created next to the exe.

Options:

- `--no-pause`: do not wait for a key press at the end. The pause happens only when the exe was started by double-click.
- Any setting can be overridden on the command line (`--Inspection:ConnectionTimeoutSeconds=10`) or through environment variables (`Inspection__ConnectionTimeoutSeconds=10`).

Exit codes:

| Code | Meaning |
|---|---|
| `0` | Inspection completed. Partial failures are listed in the report. |
| `1` | Unexpected fatal error. |
| `2` | Invalid configuration. |
| `3` | Cancelled with Ctrl+C. |

### Configuration (`appsettings.json`, section `Inspection`)

| Key | Default | Meaning |
|---|---|---|
| `ConnectionTimeoutSeconds` | `5` | Login timeout per attempt (1–30). |
| `CommandTimeoutSeconds` | `30` | Per metadata query (1–300). |
| `AllowSampleRows` | `false` | Master switch for reading business rows. |
| `MaxSampleRows` | `5` | 1–5. The code enforces a hard ceiling of 5. |
| `SampleTables` | `[]` | Tables that may be sampled, e.g. `{ "Database": "gc", "Schema": "dbo", "Table": "Computers" }`. Optional `"Server"`. |
| `AdditionalServers` | `[]` | Extra instances to probe, e.g. `".\\GAMECLASS"`, `"np:\\\\.\\pipe\\MSSQL$GAMECLASS\\sql\\query"`. |
| `EnableOdbcFallback` | `true` | Retry through the legacy ODBC driver. Required for SQL Server 2000 / MSDE. |
| `SqlLogin:UserName` / `SqlLogin:Password` | empty | Optional explicit SQL login. **Pass the password as the environment variable `Inspection__SqlLogin__Password`; never write it into the file.** |
| `OutputDirectory` / `LogDirectory` | `output` / `logs` | Relative paths are resolved next to the exe. |

The configuration is validated at startup. Invalid values stop the tool with exit code 2 and a list of problems.

## 6. Output files

| File | Content |
|---|---|
| `output/inspection-YYYYMMDD-HHmmss.json` | `environment`, `sql_instances` (including every connection attempt and hint), `databases`, `gameclass_candidates` (confidence, score, reasons, interesting structures), `schemas` (full structure of every database with confidence ≥ LOW), `sample_rows` (only if enabled), `errors`. |
| `logs/inspector-YYYYMMDD.log` | Timestamped log, appended per day. `INF`: normal discovery. `WRN`: permission/access problems. `ERR`: unexpected failures. |

## 7. Troubleshooting

| Symptom | Likely cause / what to do |
|---|---|
| `No local SQL Server / MSDE instance was found` | GameClass may use a database on another PC; check GameClass settings for the server address. Add it to `AdditionalServers` only if it is on the local network and the club agrees. |
| `Service ... is Stopped` | The inspector never starts services. Ask the operator to start GameClass normally, then re-run. |
| `Login failed` / hint about elevation | On MSDE and SQL Server 2000–2005, local administrators get in only from an **elevated** process. Right-click the exe and choose **Run as administrator**. |
| `Login failed` while elevated | The Windows account has no SQL login. Ask which SQL login GameClass uses and pass it via `Inspection__SqlLogin__UserName` / `Inspection__SqlLogin__Password`. |
| `Internal connection fatal error` with SqlClient, then ODBC succeeds | Expected on SQL Server 2000 / MSDE. SqlClient does not support TDS 7.1, so the ODBC fallback handles it. |
| ODBC fallback also fails | Check `odbcad32.exe` → *Drivers* for "SQL Server". Do not install or reconfigure anything on the club PC without approval. |
| TLS / SSL / handshake errors | Old servers only speak TLS 1.0, which modern Windows may disable. Report it; do **not** change system TLS settings. |
| Instance "did not answer" | Wrong instance name, or the service is not running. Local connections use shared memory / named pipes, so **TCP/IP does not need to be enabled.** You can try the explicit pipe name in `AdditionalServers`. |
| A database shows `HAS_DBACCESS = 0` | The login cannot open it. It is listed, not inspected. |
| `File logging disabled` | The exe folder is not writable, e.g. Program Files. Copy the tool to a writable folder such as `C:\ManajoyInspector`. |
| Garbled `✓` or Cyrillic in the console | Use Windows Terminal or a console font such as Consolas. The JSON report is UTF-8 either way. |

## 8. Running on the gaming club operator PC

1. **Build** on a developer machine: `dotnet publish ... -r win-x64` (or `win-x86`, see §4).
2. **Copy** `Manajoy.GameClass.Inspector.exe` and `appsettings.json` to a USB stick. Nothing else is needed.
3. On the operator PC, copy both files into a writable folder, e.g. `C:\ManajoyInspector\`. It can run while GameClass is running.
4. **Right-click the exe → Run as administrator.** This is needed on MSDE when the operator account is a local administrator; see Troubleshooting. Nothing is installed.
5. Read the summary at the end of the console window, then press a key to close it.
6. **Send back** the whole `C:\ManajoyInspector\output\` and `C:\ManajoyInspector\logs\` folders.
7. If the report says *Login failed* and the club can give us the SQL login GameClass uses, re-run from `cmd` with the credentials in environment variables. They are never written to disk:

   ```bat
   cd /d C:\ManajoyInspector
   set Inspection__SqlLogin__UserName=<login>
   set Inspection__SqlLogin__Password=<password>
   Manajoy.GameClass.Inspector.exe
   ```

8. Do **not** enable `AllowSampleRows` on the first visit. Turn it on later, after reviewing the structure, and only for specific tables we choose together:

   ```bat
   Manajoy.GameClass.Inspector.exe --Inspection:AllowSampleRows=true --Inspection:SampleTables:0:Database=<db> --Inspection:SampleTables:0:Table=<table>
   ```

## Solution layout

```
src/Manajoy.GameClass.Inspector/
  Program.cs                   host, DI, Ctrl+C, exit codes
  InspectionRunner.cs          stage orchestration (reusable from a future Windows Service)
  Configuration/               InspectorOptions + validator
  Discovery/                   instance sources, connection probe, database discovery
  Analysis/                    domain vocabulary + GameClass candidate scorer
  Inspection/                  table/view/column/routine inspectors, sample rows, masking
  Models/                      report records
  Output/                      console + JSON reporters
  Infrastructure/              connection factory, read-only guard/executor, SQL catalog, logging
tests/Manajoy.GameClass.Inspector.Tests/   guard, masking, scoring, naming, options

src/Manajoy.GameClass.Bridge/          Phase 2: read-only live-status bridge (Windows Service + local HTTP API)
  GameClass/                   SQL catalog, reader, row models, text repair
  Snapshot/                    wire model, state derivation (SnapshotBuilder), change detection
  Hosting/                     polling worker, snapshot store, file logger
  Api/                         read-only HTTP endpoints
  install-service.ps1          service installer (DPAPI-protected SQL password)
tests/Manajoy.GameClass.Bridge.Tests/      state derivation, queries vs guard, options, hosts, events
```

See `src/Manajoy.GameClass.Bridge/README.md` for the bridge.
