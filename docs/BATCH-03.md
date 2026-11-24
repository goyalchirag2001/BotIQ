# Batch 03 — Browser Discovery Lab (v0.3)

This incremental update assumes Batch 02 is installed. It preserves the existing investigation API and bot registry. New browser discovery routes operate only against the controlled synthetic provider at `http://127.0.0.1:5071`.

## What changed

- Editable and locally reusable workflow: fill, click, wait; fixed lab target and selector allowlist.
- Isolated Chromium context per run; capture multiple fetch/XHR responses, including unrelated traffic.
- HAR JSON import (`log.entries`, optional base64 body); only synthetic local API requests, up to 500 entries scanned and 30 accepted. Does not replay HAR requests.
- Fingerprints retain method, path (without query), status, allowlisted content-type/contract header, operation name and JSON path/type sets. No raw response or request body or authorization header is returned or persisted by the new module. Field names, paths and operation names may themselves be sensitive, so do not use confidential HARs. This is not a general-purpose redaction system.
- Candidate ranking: method 15, endpoint 15, operation 10, request path similarity 20, response path similarity 40. Scores below 45 or a top-two margin below 10 do not auto-select. Scores are not calibrated confidence probabilities. Structural similarity does not establish semantic equivalence.
- Compare selected candidate, including added/removed request fields and nullable response fields. Optional source snapshot maps differences to candidate Roslyn source locations. The browser probe never executes uploaded source.
- 39 C# assertions in the existing test runner (10 additional), plus new browser/HAR API smoke test.

## Install on Windows

Stop both running lab servers. Extract `BotIQ-v0.3-update.zip` from Downloads into the existing repo with `Expand-Archive -Force`. Build `BotIQ.slnx` and run `dotnet run --project tests/BotIQ.Tests --no-build`. No new NuGet packages or browser binaries are required beyond v0.2.

Restart `node demo-provider/server.mjs` and `dotnet run --project src/BotIQ.Api --no-build` in separate terminals.

## Demo

1. Open `http://127.0.0.1:5070/discovery.html`.
2. Restore provider v1. Capture browser traffic. Select `POST /api/hotel/search` with `HotelSearchV1` as baseline (not `/api/ping`). Alternatively capture a synthetic HAR in browser DevTools while performing a rate search against v1, then import it. Ensure the HAR includes response content for reliable matching.
3. Optionally select the source snapshot registered in Batch 02.
4. Break provider v2. Click **Capture live & compare**. Expect `POST /api/v2/availability` / `PropertyAvailability` to rank above the ping calls. Inspect the endpoint, operation, header, field and schema differences and candidate source locations.
5. With both servers running, execute `node scripts/discovery-smoke.mjs`, `node scripts/api-smoke.mjs`, and `node scripts/registry-smoke.mjs` sequentially. Tests modify synthetic provider state and reset it to v1.

## API

All new endpoints are POST, with JSON bodies and `X-BotIQ-Lab: 1`.

- `/api/discovery/capture`: `{name, steps:[{action,selector,value}]}` returns `workflow`, `requests`, `truncated`.
- `/api/discovery/har`: HAR document returns eligible fingerprints.
- `/api/discovery/compare`: `{baseline,workflow,botId?}` captures current traffic and returns `capture`, `match`, `impact`. Baseline is chosen by the user from a prior capture/import. It is not an attested artifact.

## Limits and next steps

A workflow is edited manually; no visual action recorder, generalized target registration, runtime source execution, automatic endpoint replay, AI or deployment exists in this batch. Workflow JSON can be saved in local browser storage; captured baseline selection is in memory and is lost on refresh. Lab is unauthenticated, loopback only. One discovery capture at a time; bounded steps, selector/action allowlists and timeouts. Do not run simultaneous provider mutation/investigation tests. Request matching is a heuristic over sampled JSON paths, not a full schema validator. Captures omit non-fetch/XHR traffic and may miss background traffic after the workflow completion marker; only the controlled mock provider is supported. Raw legacy v0.1 baseline storage is unchanged.

## Verification

Node provider smoke test, JavaScript syntax checks and archive integrity are checked in the delivery environment. .NET SDK is absent here, so C# compilation, assertions and full Playwright API tests need local verification.
