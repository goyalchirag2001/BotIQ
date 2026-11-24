# BotIQ 0.5.1 — stabilization, not Batch 06

This corrective update is a cumulative overlay of the delivered v0.1–v0.5 source. Stop the old processes first; save or commit local changes before extracting. It contains no Git history, App_Data, installed packages, captured traffic, generated frontend output or database data. Existing evidence and Git history remain on disk. The source is overwritten by the overlay.

## Why port 4200 was unavailable

Batch 05 depended on an independently running `ng serve` process. Its link did not check if that process started. A build is not a running HTTP server. The server was bound to IPv4 only, and some Windows systems resolve `localhost` to IPv6. Angular 22 also requires Node 22.22.3+ (22.x), 24.15.0+ (24.x), or 26.x. The user's exact error has not been supplied, so the underlying local failure is not confirmed.

## Full startup (Windows PowerShell)

```powershell
cd "D:\BotIQ-v0.1\BotIQ"
node --version
npm --version
dotnet --version
$env:BOTIQ_BROWSER_HEADLESS = "0"
$env:BOTIQ_BROWSER_TIMEOUT_MS = "60000"
node scripts/start-local.mjs
```

Requires .NET 10 SDK, supported Node version and npm registry access. If Node is too old, install a supported Node LTS version from nodejs.org and reopen PowerShell. If Playwright's browser binaries are absent, after the backend build use `pwsh src/BotIQ.Api/bin/Debug/net10.0/playwright.ps1 install chromium`, then restart. PostgreSQL is optional; leave the connection string unset for local history, or set the existing valid connection string before running. The launcher builds the backend and Angular, starts provider/API/frontend, tests service identities and the API proxy, then prints READY. Keep the terminal open. Stop with Ctrl+C. Use the exact `http://127.0.0.1:4200/` URL. IPv6 localhost is also bound where supported. Logs are in `.botiq-logs/` (not committed).

To use only existing tools without an Angular build: `node scripts/start-local.mjs --lab-only`. This starts a clearly labeled lab on port 4200, not an Angular application. It still requires .NET. You may also use port 5070 for the original pages. No automatic fallback disguises a failed Angular build.

Manual frontend startup with backend/provider running: `cd frontend; npm install; npm run build; npm start` (serves built Angular). `npm run dev` is the original Angular CLI development server. Do not run both on 4200.

## Verification

In a second PowerShell window:

```powershell
cd "D:\BotIQ-v0.1\BotIQ"
node scripts/frontend-smoke.mjs
node scripts/verify-local.mjs
node scripts/verify-local.mjs --full
```

The first suite uses mock backend/build fixtures; the second checks actual local routes and APIs; the full suite includes all five earlier API tests. Tests change synthetic provider versions and create local synthetic snapshots; don't run concurrent browser captures or use internal data. Restart using the same history configuration and run `node scripts/batch05-smoke.mjs --history-only` for persistence.

## Browser

Navigate to `/discovery` in Angular or `/discovery.html` in the legacy lab. Start provider v1, capture, select `HotelSearchV1`, switch v2, compare. A status poll now shows the capture stage. Check `http://127.0.0.1:5070/api/discovery/progress`, `.botiq-logs/api.log`, and `.botiq-logs/provider.log` for errors. The target remains `http://127.0.0.1:5071/` only. The earlier Chromium navigation changes are retained; no bypass of browser administration policies is introduced. Browser issue not confirmed fixed without a local successful run.

HAR cap remains 100 MiB, 16 million characters per body, 20,000 entries, 500 imported matches. Non-lab URLs are filtered; no production capture or arbitrary DLL execution. The frontend gateway streams request bodies instead of decoding them. Angular uses a built bundle, route refreshes, relative API requests and links to the legacy tools. Angular and API share port 4200 via the gateway.

## Verification performed in authoring environment

Provider HTTP contracts and frontend gateway tests passed; JavaScript syntax and archive integrity checked. Actual .NET / Angular build, Playwright navigation, PostgreSQL and Windows process lifecycle remain unverified here (missing SDK, incompatible Node, registry DNS unavailable, restricted Chromium). This is a stabilization candidate, not a production-readiness claim.
