# BotIQ v0.5.3 — Native Angular evidence workspace

Apply on top of v0.5.2 (which requires v0.5.1). Stop running processes; preserve local changes first.

```powershell
cd "D:\BotIQ-v0.1\BotIQ"
Expand-Archive -Path "$env:USERPROFILE\Downloads\BotIQ-v0.5.3-angular-evidence.zip" -DestinationPath "D:\BotIQ-v0.1\BotIQ" -Force
node scripts/start-local.mjs
```

Wait for `READY` (Angular mode), then open http://127.0.0.1:4200/discovery. Use Ctrl+F5 if an old bundle is cached. Do not use `--lab-only` to demo Angular. The normal startup script rebuilds Angular. If the Angular build fails, inspect `.botiq-logs/frontend-build.log` and `.botiq-logs/frontend-install.log`.

## Demo

1. Choose **Discovery & evidence** in the sidebar. **External HAR & C# analysis** is the default mode.
2. Enter the exact target hostname (e.g. `szallas.hu`, without a scheme or path).
3. Select a HAR (up to 100 MiB), optionally a C# file (up to 8 MiB), and optionally a second HAR (up to 100 MiB). Click **Analyse evidence**.
4. Inspect counts, source calls, URL and call lines, candidate query/header/payload field differences, and missing evidence. Use **Select for network comparison** to select a candidate; choose another entry or a second HAR entry and click **Compare selected requests**.
5. For automated local discovery, choose **Synthetic browser lab** on the same route. This is the existing synthetic workflow; it is not an external capture feature.

Both the Angular component and legacy evidence page use the same canonical `src/BotIQ.Api/wwwroot/evidence-engine.js` module. The Angular build bundles it directly, with a TypeScript declaration alongside it. No new dependency or backend route. Files remain in browser memory; the external reports are not saved into the backend investigation history. They compare structural evidence and header names, not secret values or actual prices. The engine matches source calls heuristically; absent requests do not prove a change. Large files can take time to analyse on the browser's main thread. Up to 20,000 HAR entries and 500 eligible entries; schema sampling remains bounded. Exact hostname matching does not include subdomains. External capture is still manual using authorised browser DevTools or the existing recording script.

## Regression tests

```powershell
node scripts/angular-evidence-smoke.mjs
node scripts/evidence-smoke.mjs
node scripts/frontend-smoke.mjs
```

Optional private local test (do not commit inputs):

```powershell
node scripts/angular-evidence-smoke.mjs "$env:USERPROFILE\Downloads\szallas.har" "$env:USERPROFILE\Downloads\Crawler.cs" "szallas.hu"
```

Actual supplied files: 705 total, 187 scoped, 39 eligible, 3 source call candidates; reservation-summary not observed. Static route checks, shared engine tests, component methods using stand-in signals, isolated TypeScript typecheck and gateway regression passed. Full Angular template compilation and browser UI testing require a compatible Node version and installed Angular dependencies; not verified in the packaging environment. No user HAR, crawler, report or credentials are included in this update.

## Commit

Review staged files; never commit proprietary source, HAR recordings, credentials or local data.

```powershell
git add frontend/src/app/platform.ts frontend/src/app/external-evidence.ts frontend/src/app/external-evidence.html frontend/src/app/external-evidence.css src/BotIQ.Api/wwwroot/evidence-engine.js src/BotIQ.Api/wwwroot/evidence-engine.d.ts scripts/frontend-server.mjs scripts/angular-evidence-smoke.mjs docs/BATCH-05-ANGULAR-EVIDENCE.md
git diff --cached --stat
git commit -m "fix: integrate external HAR and C# evidence into Angular discovery"
git push origin main
git tag -a v0.5.3 -m "BotIQ v0.5.3 - Native Angular Evidence"
git push origin v0.5.3
```
