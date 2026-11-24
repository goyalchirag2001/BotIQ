# BotIQ v0.2 — Source onboarding and code impact

This is an overlay on v0.1, not a new repository. Stop the .NET service before applying. Extract into the existing repository root. The ZIP contains only additions/changed files; existing demo provider, baseline and Git history are retained. The overlay replaces the listed v0.1 source files: commit or back up your local changes before extracting.

## Run

```powershell
dotnet restore BotIQ.slnx
dotnet build BotIQ.slnx
dotnet run --project tests/BotIQ.Tests --no-build
```

Start `node demo-provider/server.mjs` in terminal 1 and `dotnet run --project src/BotIQ.Api --no-build` in terminal 2. Reinstall Chromium using the v0.1 Playwright command only if the browser is missing. Open http://127.0.0.1:5070/bots.html. Register `HOTEL-DEMO-ANDROID`, version `1.0.0`, and select `src/BotIQ.SampleBot/HotelBot.cs`. Inspect the source evidence. Return to the integration lab, select that snapshot, then Restore v1 → Capture baseline → Break v2 → Investigate. Inspect Candidate source-code impact. Expected matches include the old endpoint, header key, operation, `hotelId`, and `translatedName` parser access.

With both servers started:

```powershell
node scripts/api-smoke.mjs
node scripts/registry-smoke.mjs
```

The registry smoke test creates one persistent synthetic snapshot per run and resets the provider to v1. The tests are executable assertion runners, not xUnit tests (`dotnet test` does not run them). The C# suite now contains 29 assertions. The registry can hold 100 snapshots; clean up disposable local data by stopping the API and removing unwanted snapshot JSON files from `src/BotIQ.Api/App_Data/bots` (depending on the API content root).

## New endpoints

| Method | URL | Purpose |
|---|---|---|
| GET | `/api/bots` | List snapshot metadata |
| GET | `/api/bots/{id}` | Read a snapshot's analysis |
| POST | `/api/bots` | Register source snapshot; JSON `{name,version,files:[{name,content}]}` |
| POST | `/api/bots/{id}/demo-investigation` | Run existing synthetic investigation and map historical-vs-live findings to selected source |

POST requests require `X-BotIQ-Lab: 1`. Source snapshots and their SHA-256 hashes are persisted locally (gitignored). Same content and normalized filenames produce the same hash; each registration creates a new ID. The hash does not attest to the deployed DLL. No source is loaded, compiled or executed. API supports relative file paths; the browser picker uses filenames and rejects duplicates. The analyzer uses Roslyn syntax trees, not compilation or semantic binding. It extracts literal relative endpoints, operation names, header keys, anonymous payload members, JSON-looking POST string fragments, HTTP call candidates, literal JSON parser calls and string indexers. Source-to-finding links are either exact literal/key matches or heuristic leaf-name matches, never confirmed runtime causes. Dynamic URLs, arbitrary JSON string concatenation, all code paths, regular expressions and dependencies are not fully resolved. The selected uploaded snapshot is never executed; the existing fixed synthetic bot and browser probe are used for the live lab. There is no arbitrary URL support. Source and evidence are plaintext on disk; only synthetic/non-confidential code should be used. No employer source or production data should be stored or committed. Do not expose the local API externally.

## Verification

Node provider HTTP tests and JavaScript syntax checks are run in the packaging environment. The .NET SDK is absent here, so Roslyn compilation, C# assertions, registry smoke and end-to-end browser tests need to be run locally. The earlier v0.1 build succeeded on the user's machine, not evidence of v0.2 compilation.

## Next batch

Parameterized recorded browser workflows, multi-request discovery/ranking, historical HAR ingestion, then semantic/data-flow analysis and generalized sandboxed bot execution. Angular and PostgreSQL will replace the temporary lab UI and local registry as the engine stabilizes.
