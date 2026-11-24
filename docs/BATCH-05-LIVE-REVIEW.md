# BotIQ v0.5.4 — live recording and revised evidence review

Incremental update for v0.5.3. Primary UI: http://127.0.0.1:4200/discovery (External HAR & C# mode). No company code, captures or credentials included. This is a local single-operator lab, not a production SaaS or authenticated service.

## Windows startup
Stop BotIQ before extracting. Back up local modifications and ensure v0.5.3 is installed. From PowerShell:

```powershell
cd "D:\BotIQ-v0.1\BotIQ"
Expand-Archive -Path "$env:USERPROFILE\Downloads\BotIQ-v0.5.4-live-review.zip" -DestinationPath "D:\BotIQ-v0.1\BotIQ" -Force
# Set only hosts that your organisation/provider authorises you to record.
$env:BOTIQ_APPROVED_CAPTURE_HOSTS = "szallas.hu"
node scripts/start-local.mjs
```

The last command builds .NET, installs/builds Angular and starts the frontend gateway. Wait for READY. Use 127.0.0.1:4200/discovery and Ctrl+F5. The external recorder is served by scripts/frontend-server.mjs, not the .NET service or ng serve. Offline HAR analysis works even with no approved hosts. Node and .NET prerequisites are unchanged. pwsh and the Playwright Chromium binaries must be installed. If required, after a successful backend build run `pwsh src/BotIQ.Api/bin/Debug/net10.0/playwright.ps1 install chromium`.

## Demo
1. Enter exact target hostname; select first HAR and C# source; Analyse evidence.
2. Review source candidates and parser-marker table. One HAR is evidence of coverage, not a historical change. Load a second HAR or launch the recording browser.
3. To record, enter HTTPS URL on the configured exact host, tick authorization and launch. Complete the authorised read-only workflow manually. Do not sign in, submit personal data, reservations or payments. Close recording browser and inspector normally. The gateway imports exact-host structural inventory from the temporary HAR and Angular runs the comparison automatically. A five-minute time budget applies. If navigation is blocked, use an authorised DevTools capture instead; never bypass website or IT controls.
4. Review candidate, possible replacement, ambiguous, not-observed states. Inspect matched contract, export JSON or save numeric summary with optional registered bot snapshot. Existing History page shows the saved summary (local or PostgreSQL). Source association is user-selected, not hash-verified. Raw file inputs are processed in browser memory; live raw HAR is temporarily stored in gitignored App_Data/external-capture and removed on normal completion. Interrupted processes or failed cleanup may leave local files. Structural paths and keys may contain sensitive information. Do not share without review.

## Matching and limits
Same HTTP method required. Score: exact path 45, response structure up to 35, request keys up to 10, query keys up to 10. Changed-path candidates require at least 75% response-shape overlap and recorded bodies; 10-point lead over second candidate. No confidence probability. Up to 100 relevant baseline requests compared against up to 500 current inventory entries. If source matches exist, compare those baseline entries; otherwise review first 100 eligible entries. Automatic matching does not prove equivalence or one-to-one assignment. Repeated hits may be ambiguous. Manual comparison remains available. Header names and payload/response structures, not credential or dynamic values. Parser marker check is literal and heuristic; no arbitrary regex execution or C# execution. No LLM calls or automatic code repair. Raw imported HAR limit 100 MiB, bodies 16 million characters, 20,000 HAR entries. The live HAR cap is checked after the recorder exits, not a hard disk quota while recording.

## Regression tests
```
node scripts/review-smoke.mjs
node scripts/frontend-smoke.mjs
node scripts/angular-evidence-smoke.mjs
node scripts/review-smoke.mjs "$env:USERPROFILE\Downloads\szallas.har" "$env:USERPROFILE\Downloads\Crawler.cs" "szallas.hu"
```
An optional fourth argument supplies a second HAR. Actual evidence: 705 total, 187 exact-host, 39 eligible, 3 source call candidates; first GET 2 same-method candidates; booking GET 1; reservation summary POST 0. Literal `rooms` marker 0/2 candidate bodies at source line 215; `priceData` 0/0 at 502 (no comparable body). No confirmed change with one capture.

Verified Node analysis, gateway and mock recording lifecycle, authorization checks, cleanup, TypeScript with test declarations, actual supplied input. Real .NET build, Angular template compilation, external browser recording and PostgreSQL persistence remain unverified here. Before committing, verify local UI and history save/restart. Do not tag a release until local tests pass.
