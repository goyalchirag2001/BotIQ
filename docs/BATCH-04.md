# Batch 04 — Advanced source/runtime intelligence and capture reliability

Incremental overlay for v0.3, using .NET 10 and existing packages. Stop both servers before extracting. No database migration; existing App_Data and Git history stay unchanged.

## Capture reliability

The previous workflow used 4-second action, 6-second navigation, 10-second launch and 4-second response-read limits. This could cause a timeout, but the user's actual failing stage has not been reproduced. Both main and discovery flows now use a shared BrowserProbe with a single capture gate, a direct loopback provider preflight, DOMContentLoaded navigation, explicit provider completion/error marker, 60-second default per-stage timeout, bounded body reading, and stage-specific error messages. Browser runs remain restricted to the synthetic provider. **Restart both the provider and the API**: the updated provider page exposes `data-state` required by the new workflow.

PowerShell (API terminal):

```powershell
$env:BOTIQ_BROWSER_TIMEOUT_MS = '60000'
dotnet run --project src/BotIQ.Api --no-build
```

Set `BOTIQ_BROWSER_HEADLESS=0` to see Chromium, or increase `BOTIQ_BROWSER_TIMEOUT_MS` to `90000` (range 15000–120000). Run `Invoke-RestMethod http://127.0.0.1:5071/api/state` to confirm provider availability. If Chromium is missing, run `pwsh src/BotIQ.Api/bin/Debug/net10.0/playwright.ps1 install chromium`. If PowerShell 7 is missing use `powershell -ExecutionPolicy Bypass -File src/BotIQ.Api/bin/Debug/net10.0/playwright.ps1 install chromium` for this installer only. Open provider manually, click Find rates, then inspect discovery error stage. A preflight error means provider connectivity, not missing browser. A launch error may mean missing Chromium or local security restrictions. Do not disable security controls.

## Limits

- HAR file/request body 100 MiB (104,857,600 bytes); direct File upload, not browser JSON stringify.
- HAR input up to 20,000 entries; first 500 eligible synthetic API fingerprints; 100,000 combined paths.
- Request/response body 16,000,000 UTF-16 characters each, up to 5,000 distinct paths and 500,000 visited nodes per shape. A capped/invalid schema is incomplete and excluded from baseline selection. Browser captures limited to 100 eligible responses / 32 million cumulative characters.
- C# source 100 files, 2,000,000 characters each and 8 MiB total UTF-8.
- Large uploads are parsed in memory; split unusually large files. HAR import still filters to 127.0.0.1:5071/api/ and JSON schema analysis, not production sites. Raw HAR values are not persisted by the discovery feature; paths and field names may themselves contain sensitive data. Synthetic samples only.

## Advanced intelligence

`/intelligence.html`: select any existing snapshot (no re-upload needed). Lists methods, candidate name-based call edges, identifier dependencies in assignments/HTTP calls, and candidate DataTable column references. No semantic overload resolution, dynamic variable evaluation or arbitrary code execution. Runtime evidence is caller-submitted metadata, not a real execution SDK. Store and retrieve version-specific traces (ordered hits, prior dependency, latency, status, output row count/columns, parser failure). Discovery page can save and load baseline + workflow profiles attached to a snapshot. All storage remains in gitignored App_Data.

API endpoints:
- GET `/api/bots/{id}/dependencies`
- GET/POST `/api/bots/{id}/runtime`
- GET/POST `/api/bots/{id}/profiles`

POST requires `X-BotIQ-Lab: 1`. Local development only. No auth or production deployment.

## Validation

Run `dotnet restore BotIQ.slnx`, `dotnet build BotIQ.slnx`, `dotnet run --project tests/BotIQ.Tests --no-build` (47 assertions expected). With both updated servers running, run `node scripts/batch04-smoke.mjs`, `node scripts/discovery-smoke.mjs`, `node scripts/api-smoke.mjs`, and `node scripts/registry-smoke.mjs`. The new smoke test uploads a 6-million-character response in a HAR and 300,000-character source, validates persistence, and exercises browser capture.

Local verification in authoring environment: Node provider HTTP smoke passed; embedded JS and .mjs syntax checked. .NET SDK unavailable; C# compilation, API smoke and Playwright browser capture not verified. The timeout fix is an implementation change, not proof the Windows issue is resolved. Full semantic analysis, live instrumentation, Angular and PostgreSQL remain future work.
