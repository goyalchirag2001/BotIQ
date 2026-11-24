# BotIQ — Autonomous Integration Intelligence

**v0.2 / local proof of concept**. A developer-infrastructure project that detects breaking provider contracts by comparing a known-good baseline, an existing C# bot, and independently captured browser traffic. All data and endpoints are synthetic. No employer assets, credentials, real provider data or production connectivity are included.

## Batch 02

Versioned bot source registration, Roslyn syntax analysis and candidate source-impact mapping are now included. See [Batch 02 instructions](docs/BATCH-02.md). The sections below describe the original v0.1 lab; its endpoints and workflow remain available.

## First milestone

1. Start a local mock hotel provider with a functioning v1 API.
2. Execute the sample C# bot (a class library compiled as a DLL) and a real Chromium browser session. Save a known-good baseline only when both agree.
3. Change the mock provider to v2 without recompiling the existing bot.
4. Re-execute the old bot and independently discover the new browser request. Compare endpoint, selected headers, operation name, payload structure, response schema and parser outcome.
5. Show the evidence and deterministic root-cause hypotheses in a local dashboard.

The browser follows a fixed recorded-style workflow (fill property field, click search). Its network predicate does not hardcode the provider API path or operation. This demo has exactly one POST fetch/XHR for that search action; multi-request ranking is a later milestone. Both executions use the same synthetic scenario. No LLM is used in v0.1.

## Prerequisites

- .NET 10 SDK
- Node.js 22+ (provider has zero npm dependencies)
- Playwright Chromium browser binary; the first install needs network access.
- PowerShell 7 (`pwsh`) for Playwright installation commands below.

From the repository root:

```powershell
dotnet restore BotIQ.slnx
dotnet build BotIQ.slnx
pwsh src/BotIQ.Api/bin/Debug/net10.0/playwright.ps1 install chromium
```

On Linux CI you may need `pwsh src/BotIQ.Api/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium` (OS package installation requires appropriate permissions).

**Terminal 1**

```sh
node demo-provider/server.mjs
```

**Terminal 2**

```sh
dotnet run --project src/BotIQ.Api --no-build
```

Open **http://127.0.0.1:5070** and click the four buttons in sequence: Restore provider v1 → Capture baseline → Break provider (v2) → Investigate. Inspect the two comparison tables, root cause hypotheses and raw evidence. The mock website can also be opened at http://127.0.0.1:5071.

Expected output for the broken scenario (not a verified .NET run): old bot HTTP 404 and zero rates; live browser HTTP 200 with two rates; endpoint and operation changed; header version changed; `hotelId` became `propertyId`; `translatedName` accepts null; the legacy parser fails on the live response. The parsing failure is tested separately against the browser response, not inferred from the 404.

## Tests

```sh
dotnet run --project tests/BotIQ.Tests
# With both servers stopped:
node tests/provider_smoke.mjs
# With both servers running:
node scripts/api-smoke.mjs
```

The first command runs 29 C# assertions (including Roslyn analysis). The second tests the provider HTTP contract; the third runs the full API workflow and resets the mock provider to v1. An independent browser/provider smoke test is also included (stop any running provider first):

```sh
python -m pip install playwright
python -m playwright install chromium
python tests/browser_smoke.py
```

If a compatible system Chromium is installed, the Python test detects it or accepts `BROWSER_EXECUTABLE`.

## Layout

```text
BotIQ.slnx
src/BotIQ.Core/          # contracts and deterministic structural diff
src/BotIQ.SampleBot/     # legacy C# bot / compiled class library
src/BotIQ.Api/           # ASP.NET Core API, Playwright probe, orchestration
  wwwroot/              # temporary vanilla dashboard
  App_Data/             # generated local known-good baseline (gitignored)
demo-provider/          # synthetic Node.js website + versioned API
tests/BotIQ.Tests/      # C# assertion runner
tests/browser_smoke.py  # real Chromium smoke test of provider only
tests/provider_smoke.mjs # provider HTTP smoke test (no browser)
scripts/api-smoke.mjs   # end-to-end API test (requires .NET servers)
docs/ROADMAP.md
```

## API

| Method | Path | Description |
|---|---|---|
| GET | /api/state | Local mock version and baseline |
| POST | /api/provider/1 | Restore healthy provider |
| POST | /api/provider/2 | Activate breaking changes |
| POST | /api/baseline | Capture healthy baseline |
| POST | /api/investigate | Three-way comparison |

POST requests require `X-BotIQ-Lab: 1`. Both services bind loopback only. The baseline file is overwritten when recaptured. There is no authentication, multi-tenancy, queue, arbitrary uploaded bot execution, AI or automatic patching. v0.2 adds static C# source registration and analysis. Do not expose these services publicly or use them with confidential data. The demo only captures allowlisted synthetic request headers; body retention is local and unredacted because all data are synthetic. A production capture design needs redaction, retention controls and security reviews first.

## Verification status

The provided environment had Node.js and Chromium but no .NET SDK and no access to NuGet restore. The Node provider HTTP smoke test passed (both versions, invalid contract rejection and null response). The independent real-browser smoke test was attempted but blocked by the environment’s Chromium policy (`ERR_BLOCKED_BY_ADMINISTRATOR` on loopback navigation). The v0.1 build succeeded on the user’s machine. The v0.2 .NET build, 29 C# assertions, registry smoke test and real-browser end-to-end tests must still be executed locally or in CI. The UI is not an Angular app yet. Original root cause messages refer to known demo methods; selecting a source snapshot in v0.2 additionally displays Roslyn-derived candidate locations. No GitHub repository has been created or modified.

## References

- Playwright .NET network API: https://playwright.dev/dotnet/docs/network
- Playwright .NET package: https://www.nuget.org/packages/Microsoft.Playwright/1.64.1
