---
type: Playbook
title: Verification
description: Test changed behavior offline and measure search quality without overstating live results.
tags: [tests, eval, quality]
status: stable
generated: { by: codex/gpt-6.1-sol, at: 2026-10-25T22:20:21Z }
sources:
  - resource: ../docs/RUNNING.md
    title: Commands and local smoke checks
  - resource: ../docs/FRONTEND.md
    title: Frontend contract and interaction checks
  - resource: ../docs/adr/0005-fixtures-and-eval-not-live-tests.md
    title: Offline tests and live eval separation
  - resource: ../eval/README.md
    title: Evaluation datasets and honesty rules
  - resource: ../docs/DEPLOYMENT.md
    title: Production release and live verification
---

# Checks

- Server changes: focused `dotnet test` for the owning project; run the full solution when a boundary changes.
- Web changes: `npm run check`; run `npm run test:e2e -w web` for rendered routing, search, responsive, or accessibility behavior.
- API shape changes: update C# contracts and OpenAPI examples, web types and snapshot, then contract tests.
- Search quality changes: use offline golden replay and compare evaluation runs. Golden and hard sets are in-sample; never tune to the sealed held-out set. Live evaluation spends network calls and may use paid AI, so label provider, catalog mode, misses, and cost.
- Production releases: the unified `ci` workflow deploys pushes to `main` only after the server, web, secret-scan, and startup checks pass. The Fly service check and post-deploy HTTP probe confirm liveness; diagnose upstream search behavior separately.

Report commands and observed results. Do not call a source edit alone a rendered or live verification.
