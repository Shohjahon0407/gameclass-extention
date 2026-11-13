# Manajoy GameClass Bridge (GC-Bridge v0.5, read-only)

Phase 2 of the Manajoy ↔ GameClass connector. The bridge runs on the club's GameClass server PC, next to MSDE/SQL Server 2000. Every 5 seconds it reads the GameClass database with a read-only SQL login and serves the result on a local HTTP API:

- the PC list with live state: free / busy / reserved / maintenance / offline;
- the current session per PC: start, planned end, time left, tariff, prepaid amount or postpay limit, spent so far;
- upcoming reservations;
- tariffs with their time-of-day variants;
- zones (computer groups) with counts and "next free in";
- a stream of status-change events.

It never writes to GameClass. Optionally it **pushes** each snapshot to the Manajoy backend (section 4a), because the backend cannot reach a club's LAN.

## 1. Read-only guarantee

| Guarantee | How |
|---|---|
| Only `SELECT` | All SQL is in `GameClass/GameClassQueries.cs`. `ReadOnlyGameClassDb` is the only executor, and it runs every statement through the inspector's `ReadOnlyQueryGuard` (the same file, linked). Unit tests run every query through the guard. |
| Read-only login | Use `gcbridge_ro` with only the `db_datareader` role. Create it with `sp_addlogin` + `sp_grantdbaccess` + `sp_addrolemember 'db_datareader'`. |
| No locking of GameClass | Every query uses `WITH (NOLOCK)`. No transactions are opened. |
| No personal data | Account ids only (`has_account`). Names, phones, passwords and operator free-text descriptions are never selected. |
| No write endpoints | The HTTP API has GET routes only. |

## 2. How state is derived (verified in the lab)

GameClass keeps live state partly in memory, so the bridge follows the same rules GCServer itself uses (`SessionsSelect`):

| `gc_state` | Rule (evaluated against SQL `GETDATE()`) | `state` (iCafe vocabulary) |
|---|---|---|
| `in_session` | `Sessions.status = 1` and `SessionsAdd.Start <= now < Stop` | `busy` |
| `maintenance` | the same, with tariff id 0 (GameClass "Ремонт") | `disabled` |
| `reserved` | `status = 0` (reservation not yet activated) and `Start <= now < Stop` | `busy` |
| `offline` | no session, and GCServer is still extending an `Uncontrol` row for the PC (lost link to the client) | `offline` |
| `free` | none of the above | `available` |

- **GCServer heartbeat.** GCServer updates the hidden `Club` row of `Uncontrol` about every 5 s while an operator is logged in. If the heartbeat is older than 5 minutes (GCServer closed, or only a manager is logged in) or missing (GameClass "Clear statistics" deletes it until the next operator logon), every PC's `state` is `unknown` and `/health` reports `degraded`. Without a live GCServer the DB is a frozen picture: expired sessions are not stopped there.
- **Heartbeat switched off.** If a club sets the Registry option `WriteUnControlClub=0`, GCServer never writes the heartbeat. The bridge then trusts the time-based session states, reports `server_alive: null, heartbeat_disabled: true`, and never reports a PC as offline.
- **Packets.** For packet tariffs (`is_packet: true`), GameClass rewrites the start to the packet window and charges the whole price at once, so use `ends_at` / `time_left_minutes` for these, not money.
- **Postpay.** `Sessions.CommonPay` is the credit limit, and `SessionsAdd.Stop` is only a cut-off, so `ends_at` and `time_left_minutes` are `null` and `postpay_limit` is set.
- **Reservations** in the future are listed in `upcoming_reservations`; the PC stays `available` until the reservation window starts.
- **Online vs. never connected.** The DB only records a *lost* link. A PC whose client never connected looks `available`, which is the same as the iCafe integration.
- **Times** are the SQL host's local wall-clock times. The bridge attaches the server's own UTC offset (`DATEDIFF(minute, GETUTCDATE(), GETDATE())`).
- **Names.** Some installs store Russian names as cp1252 mojibake (e.g. `Ïî óìîë÷àíèþ`). Tariff and group names are repaired to `По умолчанию`.

## 3. HTTP API (default `http://127.0.0.1:8787`)

| Route | Content |
|---|---|
| `GET /health` | `ok` / `degraded` (200), or `starting` / `stale` / `unavailable` (503). Remote callers without the key see only the status. |
| `GET /api/v1/snapshot` | Everything: `club`, `gameclass` (heartbeat), `counts`, `zones`, `pcs`, `tariffs`, `groups`. |
| `GET /api/v1/pcs?state=busy` | The PC list; filter by `state` or `gc_state`. |
| `GET /api/v1/pcs/{number}` | One PC. |
| `GET /api/v1/tariffs`, `/api/v1/zones` | Tariffs with variants; zones with counts. |
| `GET /api/v1/events?since=<seq>&limit=100` | Status changes after `since`. Sequence numbers are clock-seeded, so they keep increasing across restarts. If `gap` is `true`, re-read `/snapshot` and continue from `next_since`. |

When polling has been failing for more than 3 intervals (minimum 30 s), the data routes return **503 `stale`** instead of an old snapshot.

Each PC has an `icafe_compat` object in iCafeCloud `/pcs` field names (`pc_name`, `pc_in_using`, `status_connect_time_left`, …). The backend's existing `icafe/normalizer.py` (`normalize_pc`, `build_envelope`) consumes it unchanged; this was verified against live lab output. `unknown` states send `null` there, which the normalizer maps to `unknown`.

If `ApiKey` is set, every `/api` request needs the `X-Api-Key` header. Listening on anything other than loopback **requires** an `ApiKey` of at least 24 characters. Addresses can only be set through `Bridge:HttpUrls`; the `Kestrel:Endpoints` and `urls` settings are rejected.

## 4. Configuration (`appsettings.json`, section `Bridge`)

| Key | Default | Meaning |
|---|---|---|
| `Server` / `Database` | `(local)` / `GameClass` | GameClass SQL instance and database. |
| `Provider` | `Auto` | `Auto` uses the Windows "SQL Server" ODBC driver first (the only in-box driver for SQL 2000), then SqlClient. |
| `SqlLogin:UserName` | — | e.g. `gcbridge_ro`. The installer stores it in the service environment. |
| `SqlLogin:PasswordFile` | `bridge.secret` | DPAPI (LocalMachine) encrypted password written by the installer. `Bridge__SqlLogin__Password` in the environment also works. Never put the password in appsettings.json. |
| `PollIntervalSeconds` | `5` | 1–300. |
| `HttpUrls` | `http://127.0.0.1:8787` | `;`-separated. |
| `ApiKey` | — | Required for non-loopback URLs. |
| `SnapshotFile` | `output/snapshot.json` | Latest snapshot, replaced atomically. |
| `EventLogFile` | `output/events.jsonl` | Written as `events-YYYYMMDD.jsonl`; kept for 30 days. |
| `LogDirectory` | `logs` | `bridge-YYYYMMDD.log`; kept for 30 days. |

## 4a. Push to Manajoy

When `Bridge:Push:Url` is set, the bridge POSTs a **reduced** snapshot to the backend with an `X-Bridge-Key` header. It pushes right after a status change, otherwise every `Push:IntervalSeconds` (default 15). Nothing is received back.

What leaves the machine (`PushPayload` in `Hosting/PushWorker.cs`, pinned by `PushWorkerTests`):
- `v`, `provider`, `bridge_version`, `fetched_at`;
- `gameclass`: `server_alive`, `heartbeat_age_seconds`, `heartbeat_missing`;
- per PC: `number`, `state` and `icafe_compat` (PC name, zone name, group id, enabled / in use, minutes left).

Money, tariffs, session details, reservations and IP addresses stay on the club PC.

| Key | Default | Meaning |
|---|---|---|
| `Push:Url` | — | `https://api.manajoy.uz/api/integrations/gameclass/{seller_id}/snapshot`. Plain `http` is accepted only for loopback/private LAN addresses. |
| `Push:KeyFile` | `push.secret` | DPAPI-encrypted per-club key written by the installer (`Bridge__Push__Key` also works). |
| `Push:IntervalSeconds` / `TimeoutSeconds` | `15` / `10` | |

Backend replies: 401 = wrong key, 409 = live status disabled for the club, 422 = format rejected. On any failure the bridge backs off (5 s doubling to 5 min; immediately 5 min for 401/409) and logs the reason once. `/health` shows a `push` block with the last success, the last error and the failure count.

**Getting the URL and key (v0.3: self-service pairing).**
1. The club owner taps *Profil → Kompyuterlar holati → GameClass orqali ulash* in the ManaJoy Biznes app. The app calls `POST /api/seller/gaming/integration/gameclass/pairing-code` and shows a one-time code (`XXXX-XXXX`, valid 15 minutes).
2. The setup window (section 6) sends it to `POST /api/integrations/gameclass/pair`.
3. The backend creates the integration, or rotates its key, and turns live status on. It returns `seller_id`, `club_name`, `push_path` and the bridge key, once.
4. The window stores the key (DPAPI), sets `Bridge__Push__Url` = API base + `push_path` and restarts the service.

The setup window then reads `GET /api/integrations/gameclass/{seller_id}/status` with the same key to show the club name, whether buyers see live status, and how many PCs are linked to stations.

Fallback for support: `python manage.py gameclass_bridge_key --seller-id <id>` still creates or rotates the key manually. In that case live status starts off and needs `--enable`.

The backend feeds `icafe_compat` into the existing iCafe envelope and station sync, so buyer/seller/admin screens work unchanged. The one GameClass-specific rule: a PC the bridge reports as `offline` (lost link to its client) stays `offline` in the envelope; iCafe's `/pcs` has no such state, so it would otherwise show as available.

## 5. Build, test, install

```bash
dotnet test tests/Manajoy.GameClass.Bridge.Tests
dotnet publish src/Manajoy.GameClass.Bridge -c Release -r win-x64 --self-contained true -o publish/bridge-win-x64
```

On the GameClass PC, from an elevated PowerShell in the folder with the published files:

```powershell
.\install-service.ps1 -SqlUser gcbridge_ro      # asks for the password
.\install-service.ps1 -PushUrl https://api.manajoy.uz/api/integrations/gameclass/26/snapshot   # asks for the key
.\install-service.ps1 -DisablePush
```

Parameters left out keep their current values.

The installer copies the bridge to `C:\Program Files\Manajoy\GameClassBridge`, resets the folder to Administrators/SYSTEM-only access, and stores the password DPAPI-encrypted. It then creates or updates the `ManajoyGameClassBridge` service (delayed auto start, restart on failure) and checks `/health`. Remove the service with `.\install-service.ps1 -Uninstall`.

For a one-off diagnostic poll that prints the snapshot without starting the API or touching files, run `Manajoy.GameClass.Bridge.exe --once`.

## 6. Setup window (v0.3): one file for club owners

The exe is also the installer and the dashboard. `field-kit/build-field-kit.sh` publishes it as `out/ManajoyGameClass-Setup-<version>.exe`:
- a 32-bit build that runs on 32- and 64-bit Windows;
- about 43 MB, under Telegram's 50 MB limit; the build fails if it gets bigger.

**Antivirus (since 0.4.1).** The single-file bundle is **not compressed**. The compressed 0.4.0 download from Telegram was blocked by Microsoft Defender as `Trojan:Win32/Wacatac.B!ml`, a machine-learning verdict: a compressed bundle looks like a packed dropper. The same program uncompressed scans clean, including with the Telegram mark-of-the-web.

To stay under 50 MB, the framework is trimmed instead (`TrimMode=partial`). Only framework assemblies are trimmed; this app and its packages stay whole, so the reflection-based JSON and minimal APIs keep working.

Two consequences of trimming:
- **No `dynamic` COM.** WScript.Shell through `dynamic` crashed the trimmed process.
- **Own COM interfaces.** Shortcuts use typed `IShellLinkW`/`IPersistFile` declared in `Setup/ShellShortcut.cs`. The framework's `ComTypes.IPersistFile` lost methods to the trimmer, and Save silently wrote nothing.

Check a new build on a Defender VM: mark it with `Zone.Identifier` ZoneId=3, then run `MpCmdRun -Scan -ScanType 3 -File …`.

The lasting fix is **code signing**, plus submitting false positives at https://www.microsoft.com/wdsi/filesubmission.

| How it is started | What it does |
|---|---|
| By the Service Control Manager | The bridge service (sections 1–4a). |
| Double-click (UAC: administrator) | Opens the setup window in a Microsoft Edge app window. Without a service it starts the wizard; with one it shows the dashboard. |
| `--once` / `--run` | One diagnostic poll / the bridge in a console. |
| `--setup-demo` | The window with fake data, on any OS, changing nothing. Pairing code `TEST-2345`. |
| `--setup-headless [--setup-port=N]` | The window's API only; prints `SETUP_URL=…` (remote checks over SSH). |
| `--api-base=URL` | Pair with another backend (lab or staging). Without it, the base of the existing push URL is used, else `https://api.manajoy.uz/api`. |

**Wizard steps**
1. **Check.** The C# twin of `preflight.ps1`, plus a Manajoy reachability check.
2. **GameClass database.** Asks for the `sa` password once; on GameClass installs it is empty. The C# twin of `create-readonly-login.ps1`, which:
   - creates or resets `gcbridge_ro` (`db_datareader`);
   - DENYs EXECUTE on every GameClass procedure;
   - logs in as that login and proves writes are refused.
3. **Install.** The C# twin of `install-service.ps1`:
   - copies the exe to `C:\Program Files\Manajoy\GameClassBridge`;
   - restricts the folder to Administrators and SYSTEM;
   - stores the password with DPAPI;
   - registers the service (delayed auto start, restart on failure) and starts it;
   - waits until GameClass is read;
   - creates the desktop shortcut.
4. **Manajoy.** Pairing code (see 4a).
5. **Done.**

**Dashboard**
- Live PC state on the club map or as a list (tabs; the choice is remembered), with counts.
- Filters: click a count card (state), zone chips, search by PC number or zone. On the map, non-matching PCs are dimmed.
- Click a PC for details: GameClass state, zone, time left / postpay, next reservation, whether it is on the map.
- Polls every 4 s and patches only what changed. Scroll, map zoom, the search box and an open detail stay put.
- **Full-screen map:** opened from the header or the map's corner button; light and interactive (count filters, search, PC details). Esc goes back.
- **Monitor mode:** full screen, dark, big counts and a clock, refreshes every 3 s. Meant for a TV or second screen at the desk; Esc exits.
- **Side rail:** outside the wizard it tucks in to a narrow strip with the logo, club status dot and language. The choice is remembered.
- GameClass Server heartbeat.
- The Manajoy card: club, live for buyers, stations linked, last push.
- Actions: Update (shown when this exe is newer than the installed service; login, secrets and pairing are kept), Reconnect, Stop sending, Restart, Report (zip on the Desktop, no secrets), Set up again, Uninstall (optionally drops the SQL login).

**Club map (v0.4)**

GameClass stores no coordinates: `Computers` has no position columns, and GCServer lists PCs in a table. So the owner draws the map in the window (*Draw the map*).
- **Placing PCs.** Drag them from the tray of unplaced PCs, or click one to put it on the next free spot. *Auto-arrange* lays out a zone in rows. Points snap to 25 units.
- **Areas.** The *Areas* tab has presets: hall, VIP hall, bar, café, consoles, lounge, WC, smoking area, desk and entrance. Pick one, then drag a rectangle or click.
  - Each area has a name and a colour from a fixed palette of 8.
  - Click an area to select it, then drag: the PCs and objects inside move with it.
  - *Put unplaced here* fills the area's free spots without covering existing PCs.
- **Other objects.** Walls, text labels.
- **Editing.**
  - Undo/redo, multi-select (Shift or a rubber band), arrow keys, Delete, Ctrl+D duplicate, align in a row or column.
  - Corner handles resize; double-click edits the name.
  - Zoom with the wheel; Space + drag pans. A button switches to full screen.
- **Zones.** GameClass computer groups get automatic frames, as the backend draws them for buyers. Once the owner marks areas, the drawn areas replace those frames.

Storage and sync:
- **Coordinates.** Manajoy's club-map space, 0..1000 (`services.ResourceLayout`). PCs are keyed by GameClass number, the same `external_name` the backend maps to stations.
- **Local copy.** `layout.json` in the install folder (Administrators/SYSTEM), written atomically. Validated on save and on load:
  - at most 1000 PCs and 300 objects;
  - only known object kinds and palette colour names (never raw CSS);
  - texts of 40 characters or less;
  - file size 256 KB or less.
- **Manajoy is the source of truth for PC points.** `GET`/`PUT {api}/integrations/gameclass/{seller_id}/layout` with `X-Bridge-Key`, called by the setup process (`LayoutCoordinator`). Buyers, the Biznes app and the admin panel then show the same map.
- **Opening the map.** It pulls Manajoy's points, unless local edits are still unsent.
- **Saving.** It writes locally first, so it works offline (`pending` until the next open), then pushes with the version last seen.
- **Conflicts.** If the map changed in the Biznes app meanwhile, the owner chooses which map to keep (`conflict`); nothing is overwritten silently.
- **What is only stored locally.** Objects. Also PCs that are not linked to a Manajoy station yet (listed as *skipped*).
- **Push payload.** `pc_box_left/top` in the push stay `null` on purpose: the drawn map is the single coordinate source, so no second "icafe" source competes with it.

Window preferences (language, map/list tab) live in `%ProgramData%\Manajoy\GameClassBridge\setup-prefs.json`. The port changes every launch, so localStorage would not survive.

**Cashier assistant (v0.5)**

Click a PC on the map or in the list: its panel shows what GameClass will charge. Prices only. The session is still started in GameClass by the operator, and nothing is written anywhere.

Why prices only: GCServer keeps live sessions in memory, so sessions written to the database from outside are ignored or break on restart (see the 2026-10-06 spike in `LAB-STATUS.md`).

*What the cashier sees*
- **Tariffs:** only those of the PC's computer group (GameClass offers a tariff only when `Tarifs.idGroup` equals the computer's group). The current rate is shown.
- **Time:** cost and the real end time. GameClass rounds money up and applies the start minimum, so the end can be later than the order. A float quirk can also end it one second early (2 h at 10/h → 1:59:59). The port reproduces both.
- **Money:** duration and end time; the min/max from the Registry or the tariff are checked.
- **Packets available now:** fixed price, ending with the packet window.
- **Postpay:** the trust limit and the provisional end.
- **Busy prepaid PC:** "add money → new end", using RecalcStopTime (segment start, paid minus earlier segments and extras, plus penalty minutes).
  - It is shown only when the model reproduces the session's current end within 2 s. Otherwise the UI says it cannot be computed.
  - Postpay and packet sessions are not topped up.
- **Warnings:** the next reservation is in the way; over the maximum; extended by rounding.
- **Prices modal:** every zone's tariffs and rates.
- **Discounts:** prices are for a guest. Account discounts are not applied.

*Engine*
- `Pricing/TariffCalculator.cs` is a line-by-line port of `TTarif` (`Server/uTariffication.pas`): cheapest eligible variant per step, `>=/N` conditions, weekday strings, `roundtime` in seconds, `roundmoney` ceil/floor, packets via `TypeIntersect` / `GetWholePrevDay`.
- It uses the same double arithmetic. Checked against x87 extended precision: same sign of the remainder.
- Anything the original cannot price (no variant for a moment, an unknown condition, `roundtime` 0) is an error, never a guess.
- `Pricing/CashierDesk.cs` builds the answers. They are served by `GET /api/setup/pricing?pc=N` and `POST /api/setup/quote`.

*Data*
- The service snapshot now also carries:
  - `roundtime`, `useseparatesumm`, `startmoneymin/max` and `maximumtrust`;
  - the variant `condition`, `FreePacket` and exact seconds;
  - the Registry keys `StartMoneyMinimum/Maximum` and `MaximumTrustPostPay`, parsed with either decimal separator ("0,5");
  - the per-segment cost, penalty minutes and extras.
- It is all local: the Manajoy push still carries PC states only.
- With an older service, pricing answers `pricing_unavailable` and the window offers the update.

**Security of the window**
- **Server side.** All changes happen in this elevated process. The service's own API on `:8787` stays GET-only.
- **Listener.** The window's API listens on `127.0.0.1` on a random port.
- **Token.** Every `/api/setup` call needs a one-time token. It reaches the page only in the URL fragment, which is never sent over HTTP or logged.
- **Host and Origin.** Both must name the listener, so other web pages cannot drive it (no CSRF or DNS rebinding).
- **Page policy.** A strict CSP and no inline script.
- **Unelevated browser.** The Edge window is started through explorer, so it runs without administrator rights.
- **One instance.** A second double-click re-opens the running window instead of starting a second installer.
- **File access.** Only the exe is readable by Users, so the shortcut can show the UAC prompt; the `*.secret` files stay Administrators/SYSTEM only.

**UI files.** `SetupUi/` holds `index.html`, `app.css`, `app.js`, `map.js` (map view and editor, plain SVG, CSP-safe) and `i18n.js` (uz/ru), plus the logo and the Plus Jakarta Sans font (OFL). They are embedded resources; no build step is needed. The code is in `Setup/`.
