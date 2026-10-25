# ADR 0004: OpenAI Decisions as an opt-in status gate, never a ranker

Status: accepted (2026-10-24), off by default

## Context
Research (2,886 calls) found the Decisions API deterministic for identical input and well calibrated (picks at ≥ 0.99
were right 98.5% of the time), but input-order sensitive and wrong on the brief's own tier-B case ("hobbit dixon").

## Decision
- `IMatchJudge` sees the deduplicated candidates in code order plus a NONE option. Status becomes `match` only when code's
  #1 is tier A/B and the judge picks the same work at p ≥ 0.9. Disagreement → `ambiguous`, and the judge's pick is
  guaranteed a shown slot marked `second_opinion`.
- It never reorders candidates and never upgrades author-only or weak results. Single-word and unsupported-guess rules
  apply before it ("hermione" can never become `match`).
- `Judge:Enabled=false` by default; a missing key, refusal, timeout (1.5 s) or error falls back to the code rule.

## Consequences
A second vendor and key when enabled (~$0.08 per 1,000 searches measured in research). The gate trades coverage for
precision; the eval reports both, judge on and off.
