# ADR 0005: Recorded fixtures in tests, live quality in the eval tool

Status: accepted (2026-10-24)

## Context
Open Library is slow and flaky (research: p50 ~3 s, p90 ~7 s, a 60 s timeout, 503s, slow empty 200s) and LLMs are keyed
and non-deterministic. CI must be fast, deterministic and keyless.

## Decision
- Unit and integration tests use pure Core code, hand-written fakes and recorded Open Library JSON (WireMock.Net for the
  HTTP adapter, `WebApplicationFactory` for the API). No network, no keys.
- `tools/FindThatBook.Eval` measures the real system against versioned datasets (golden v2 for tuning-era checks, the
  sealed held-out set for final numbers) and writes `summary.md` files; README numbers come only from those files.

## Consequences
Fixtures go stale, so the eval has a `record` command and a fixture miss fails loudly instead of reaching the network.
