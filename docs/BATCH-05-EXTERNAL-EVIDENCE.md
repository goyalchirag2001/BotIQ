# External HAR evidence hotfix

The original discovery API and browser runner intentionally allow only a synthetic local provider. Do not use them for external targets. The new `/evidence.html` page reads HAR and source locally in browser memory and filters to an exact hostname; no API upload or network replay. 100 MiB per HAR, 20k entries, 500 eligible requests, 16M decoded characters per body, 8 MiB source. Structural JSON, HTML marker and form evidence only; source extraction is heuristic and not full C# parsing. Paths and field names may themselves be sensitive. No file is persisted by the page. Avoid entering confidential files on unauthorised systems.

## Install
Stop existing services; extract this incremental zip into the v0.5.1 project root. Start normally or use `node scripts/start-local.mjs --lab-only`; open http://127.0.0.1:5070/evidence.html or http://127.0.0.1:4200/evidence.html. The first URL works without building Angular when the API is running. Choose exact host and files. Optional second HAR is compared against manually selected entries.

## Capture
For authorised websites, use ordinary Chrome/Edge DevTools Network, preserve log, perform a read-only workflow and export a HAR (sanitized where possible). For an independent Playwright session, use `pwsh -File scripts/record-approved-har.ps1 -Url 'https://example.com/' -ApprovedHost 'example.com' -Authorized`. This uses standard Playwright CLI, not the synthetic route guard. It records traffic for the specified HTTPS origin, but is not an outbound browser firewall. Do not navigate outside your authorised scope. Do not circumvent site restrictions, submit transactions, or enter credentials. The recorded raw HAR is local and gitignored; delete when no longer needed. External sites may block automated browsers; this is not an access bypass. Import the resulting file into the evidence page. CLI capture not verified in the delivery environment.

## Test
`node scripts/evidence-smoke.mjs` optionally accepts HAR path, source path and host. This is independent of .NET and Angular. Existing synthetic APIs unchanged; Angular nav link and frontend proxy updated. No public deployment or automatic browser actions for external websites in this hotfix.
