---
type: Architecture
title: Server and search policy
description: Keep decisions in pure Core, external I/O in Infrastructure, and HTTP in Api.
tags: [server, search, api]
status: stable
generated: { by: codex/gpt-6.1-sol, at: 2026-10-25T21:35:55Z }
sources:
  - resource: ../docs/adr/0006-modular-monolith.md
    title: Three-project modular monolith
  - resource: ../docs/adr/0001-ai-interprets-code-decides.md
    title: Search decision boundary
  - resource: ../docs/API.md
    title: HTTP contract
  - resource: ../docs/RUNNING.md
    title: Running and configuration
---

# Ownership

- `src/FindThatBook.Core`: pure normalization, search orchestration, candidate evidence, matching, ranking, status, and explanation grounding. Keep external I/O out.
- `src/FindThatBook.Infrastructure`: Open Library, AI providers, caching, resilience, suggest index, and dependency registration.
- `src/FindThatBook.Api`: minimal HTTP endpoints, validation, contracts, OpenAPI examples, rate limits, ProblemDetails, and local admin guard.

# Invariants

Only catalog returned works may appear. Deterministic code decides result order and `match`/`ambiguous`/`none`; a topic request cannot become a single `match`. Catalog failures and deadlines must not turn an unknown result into a confident `none`. Missing or failing AI produces a valid degraded search. Keep the API's snake_case contract and examples aligned with the [frontend](frontend.md).
