# Batch 05 — Angular workspace, investigation history and Chromium navigation

Incremental update for Batch 04. Stop existing Node and .NET processes. Extract into `D:\BotIQ-v0.1\BotIQ` (not a new child folder). Preserve your local changes and App_Data.

## Browser change

The previous capture launched Chromium, created another context/tab and intercepted every network request. This update reuses the initial tab in a fresh temporary Playwright persistent context, installs a guard for disallowed URLs only, attaches capture handlers before an explicit `GotoAsync`, verifies the actual provider control and brings the page to the front in headed mode. The local provider root and state are checked first. The prior cause of your blank window is not confirmed; console stage logging and optional provider request logging are included. The new guard keeps the synthetic origin only, excluding administrative paths. This is not a general security sandbox or live external website browser. All browser sessions still navigate ONLY to the local demo.

1. `dotnet restore BotIQ.slnx`, `dotnet build BotIQ.slnx`, `dotnet run --project tests/BotIQ.Tests --no-build`.
2. Terminal 1: `$env:BOTIQ_PROVIDER_LOG = '1'`; `node demo-provider/server.mjs`.
3. Verify `Invoke-WebRequest http://127.0.0.1:5071/` returns 200. Open the URL in your normal browser and manually search.
4. Terminal 2: `$env:BOTIQ_BROWSER_HEADLESS = '0'`; `$env:BOTIQ_BROWSER_TIMEOUT_MS = '60000'`; `Remove-Item Env:BOTIQ_BROWSER_CHANNEL -ErrorAction SilentlyContinue`; `dotnet run --project src/BotIQ.Api --no-build`.
5. At `http://127.0.0.1:5070/discovery.html`, restore v1 and capture. The API terminal should report `selecting initial browser tab`, `installing restricted network guard`, `navigation to local provider http://127.0.0.1:5071/`, `Browser request: GET /`, `Browser main frame navigated`, `verifying provider controls`, then workflow steps and `POST /api/hotel/search`. The provider terminal also reports GET / and POST. The capture closes its temporary browser after the run; it is not a permanent open browser.
6. If a bundled Chromium window is still blank, stop the API and set `$env:BOTIQ_BROWSER_CHANNEL = 'msedge'` (requires installed Edge) or `'chrome'` (requires installed Chrome), then rerun. For further diagnostics set `$env:DEBUG = 'pw:api,pw:browser'` temporarily and send the last API stage and provider log, without secrets. Reinstall bundled Chromium if missing: `pwsh src/BotIQ.Api/bin/Debug/net10.0/playwright.ps1 install chromium`. To remove debug settings: `Remove-Item Env:DEBUG -ErrorAction SilentlyContinue`.

## Angular 22 workspace

Requires Node ^22.22.3, ^24.15.0 or ^26.0.0. From repository root run `cd frontend`, `npm install`, `npm run build`, `npm start`. Visit `http://127.0.0.1:4200/`. The development server proxies `/api/**` to port 5070. The old dashboard remains at port 5070. The Angular workspace has overview, bot registry, discovery and history routes; existing advanced/runtime and three-way screens are linked rather than fully ported. Saved discovery profile management is still on the legacy page. Keep the Node provider and .NET API running. No public deployment or authentication in this batch.

## Optional PostgreSQL history

Without `ConnectionStrings__BotIQ`, investigation summaries go to ignored `App_Data/history`. To use PostgreSQL, start Docker Desktop and run from root (PowerShell):

```powershell
$env:BOTIQ_DB_PASSWORD = [guid]::NewGuid().ToString('N')
docker compose up -d --wait postgres
$env:ConnectionStrings__BotIQ = "Host=127.0.0.1;Port=5433;Database=botiq;Username=botiq;Password=$env:BOTIQ_DB_PASSWORD"
dotnet run --project src/BotIQ.Api --no-build
```

Use the same password for subsequent starts against the existing volume; changing this env var does not change an initialized database role. Save the password in your own local secret manager, not in Git. The first API startup applies migration 001 (idempotent SQL in a transaction with an advisory lock), and Entity Framework Core/Npgsql stores and queries completed investigation summaries. PostgreSQL configuration is explicit; startup fails rather than silently switching stores when the configured database is unavailable. `GET /api/platform` reports active history storage. Existing source snapshots, saved profiles, runtime traces and original baseline remain as local files. History saved in file mode is not automatically copied to PostgreSQL. For the file-only mode stop the API and `Remove-Item Env:ConnectionStrings__BotIQ -ErrorAction SilentlyContinue` then restart. Do not remove the Docker volume unless you intend to delete history.

## Tests

With both servers running, `node scripts/batch05-smoke.mjs` tests provider navigation, real captured v1/v2 requests, discovery history and limits. Stop/restart the API with the same storage configuration, then `node scripts/batch05-smoke.mjs --history-only` verifies previously recorded history exists. Run `node scripts/discovery-smoke.mjs`, `node scripts/api-smoke.mjs`, `node scripts/registry-smoke.mjs`, and `node scripts/batch04-smoke.mjs` sequentially. Tests switch the synthetic provider to v1 at the end; they add test history and may add sample registry records.

## Limits and verification

The 100 MiB HAR/16M character body limits from Batch 04 remain unchanged. No external HAR or website support, real DLL execution, AI repair, production security, or full database migration of the registry in this release. Source package only: .NET SDK, PostgreSQL/Docker and Angular 22-compatible Node are not available in the packaging environment. Browser and integration runtime verification must be done on Windows. No npm lockfile is generated here; commit the lockfile created by `npm install` locally. Do not use employer source or real customer data.
