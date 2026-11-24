# BotIQ product roadmap

## Goal

An independent integration engineer that observes, diagnoses and eventually proposes validated repairs. The initial domain is synthetic hotel shopping; the engine should remain domain-independent. Company deployments require authorization and review of IP and data policies.

## Phase 1: prove the comparison engine (v0.1)

- Real browser capture and legacy bot execution for one controlled workflow.
- Deterministic contract comparison: endpoint, allowlisted headers, operation, JSON paths/types.
- Persist healthy baseline; report evidence; test legacy parser against current browser response.
- Acceptance: healthy baseline yields no drift; changed provider yields old HTTP 404 / live HTTP 200, with all six key differences identified and parser error demonstrated.
- Remaining: execute .NET integration tests in a supported environment. Expand schema model to distinguish field optionality, variable array shapes and sample-size effects; baseline approval/versioning.

## Phase 2: source intelligence and onboarding

- Roslyn solution-aware indexing of methods, string construction, HTTP calls, parser access and DataTable mappings. Track source revision and compiled artifact digest. Use runtime observations for dynamic values.
- Bot profile: ID, artifact/version, provider, platform, market, scenario, source revision, workflow and baseline.
- Import approved HAR/fixtures with automatic secret redaction. Teach mode records developer-approved browser actions and parameters. Rank multiple candidate network requests using action association, structure and business meaning; show confidence, allow manual confirmation.
- Angular frontend, ASP.NET API, PostgreSQL and migrations; authentication, tenant isolation and audit logs.

## Batch 03 progress: browser discovery

- Delivered editable local lab workflows, bounded multi-request capture, synthetic HAR import, structural fingerprints, ranked candidate matching and source-impact mapping. Build and browser verification pending.
- Remaining: actual browser action recorder, persistent versioned workflow and baseline profiles, runtime adapters, and approved generalized provider onboarding.

## Phase 3: runtime ingestion

- Define a versioned event envelope with execution ID, bot ID, DLL hash, scenario ID, request sequence, outcome and trace correlation.
- Integrate a shared execution adapter (only after actual host contracts are understood). Separate metrics from sampled payloads; configurable retention and size limits.
- Use sandboxed worker processes/containers, queue, retries, quotas, cancellation, allowlisted network destinations. Arbitrary uploaded DLLs must not execute in the API process. No credential access in untrusted workers.
- Group by provider/platform/market and detect comparable cohort output anomalies; integrate an existing data-loss alert via an approved connector.

## Phase 4: AI-assisted diagnosis and repair

- Provide sanitised, evidence-linked diffs plus relevant source snippets; treat browser and repository content as untrusted data, not instructions.
- Generate a patch in an isolated workspace, compile, run contract and business-data regression tests, compare results to approved fixtures and permitted live tests. Human approval before PR or deployment.
- Track model/prompt versions, evidence, cost, accuracy and rejected suggestions. Avoid presenting guesses as confirmed root causes.

## Phase 5: production SaaS

- Angular dashboards, organization and permissions, worker autoscaling, monitoring, billing and usage quotas. Deployment-aware incident correlation and risk-based probing.
- Evaluate mean time to diagnose, false-positive rate, confirmed request matches and successful regression checks in a pilot. Validate commercial demand before expanding scope.

## Architectural principles

- Independent browser evidence is not automatically the correct bot contract. The desktop, mobile and native apps may differ. Use matching approved platform, inputs and market.
- Observe access restrictions; no bypass, no automated writes on external sites without permission.
- Do not store secrets or arbitrary raw production traffic by default.
- Start with modular services; introduce distributed infrastructure only when justified by throughput, isolation or reliability needs.
