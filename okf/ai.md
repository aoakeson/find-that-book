---
type: Architecture
title: AI boundary
description: AI proposes interpretations and wording; code validates facts and makes decisions.
tags: [ai, prompts, safety]
status: stable
generated: { by: codex/gpt-6.1-sol, at: 2026-10-25T21:35:55Z }
sources:
  - resource: ../docs/adr/0001-ai-interprets-code-decides.md
    title: AI and code responsibilities
  - resource: ../docs/adr/0003-provider-agnostic-ai.md
    title: Provider seam
  - resource: ../docs/adr/0004-decisions-judge-status-gate.md
    title: Optional judge boundary
  - resource: ../docs/adr/0007-configuration-and-keys.md
    title: Configuration and secrets
---

# Rules

Structured LLM calls and versioned prompts live in `src/FindThatBook.Infrastructure`; Core sanitizes hypotheses and checks explanation facts. A provider can suggest titles, authors, characters, and topics, but cannot create catalog records or decide rank/status. Ungrounded explanation text falls back to a template. No key, timeout, quota, or provider error must stop the search.

The optional Decisions judge may only gate a code eligible `match` and surface its disagreement; it cannot reorder or promote weak evidence. Respect per-provider budgets and cache keys that include prompt versions. Keep keys write-only and redact them everywhere. Consult [verification](verification.md) before claiming a prompt or provider change improved quality.
