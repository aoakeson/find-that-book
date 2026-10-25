---
type: Architecture
title: Frontend
description: Render the API honestly in one accessible, responsive search interface.
tags: [frontend, ui, accessibility]
status: stable
generated: { by: codex/gpt-6.1-sol, at: 2026-10-25T21:35:55Z }
sources:
  - resource: ../docs/DESIGN.md
    title: Visual and interaction rules
  - resource: ../docs/FRONTEND.md
    title: Frontend behavior and limitations
  - resource: ../docs/API.md
    title: API contract
---

# Ownership

`web/src` is the React/Vite client. `web/src/api/types.ts` mirrors the API; `web/src/features/search` owns the composer and result states. Change server contracts in both layers and their contract tests.

# Invariants

Show candidates in server order with the server's status, explanation, and degraded reasons. Never rerank, infer confidence, fabricate missing book data, or show guessed progress. Search is an explicit, cancellable action: focus, navigation, remount, and history restore must not spend another API/LLM call. History stores only its allowlisted snapshot; keys stay out of React state and storage.

Follow [the design guide](../docs/DESIGN.md) for the single composer, restrained palette and motion, 16px editable text, keyboard support, 320px layouts, readable status text, contrast, and reduced motion. Verify rendered interactions when changing UI.
