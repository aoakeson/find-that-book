# ADR 0003: One provider-agnostic AI seam, Gemini by default

Status: accepted (2026-10-24, build 2026-10-25)

## Context
The brief asks for Gemini by default and a documented reason if another model is used. The research measured that model
strength is the biggest lever on interpretation quality, so the eval needs to compare providers.

## Decision
- All LLM calls go through `IStructuredLlm` (Infrastructure) over `Microsoft.Extensions.AI.IChatClient`, with the official
  Google.GenAI, Anthropic and OpenAI SDK adapters. `AI:Provider` = `gemini` (default) | `anthropic` | `openai` | `none`.
- Structured JSON output for both operations; versioned prompt files; per-provider options (no temperature where the
  API rejects it, thinking/reasoning minimized, SDK hidden retries disabled).
- Determinism comes from code-side ranking and caching, not from temperature.
- A provider counts as supported only after a real smoke call through our adapter passed (see `docs/ai-smoke.md`).

## Consequences
- Lowest-common-denominator options; provider quirks are isolated in one factory.
- Missing key or `none` → heuristic arm + template explanations, `degraded_reasons: ["ai_not_configured"]`.
