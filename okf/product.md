---
type: Product
title: Find That Book
description: Find an Open Library work from a messy book description without overstating certainty.
tags: [app, search]
status: stable
generated: { by: codex/gpt-6.1-sol, at: 2026-10-25T21:35:55Z }
sources:
  - resource: ../README.md
    title: Project overview and behavior
  - resource: ../docs/adr/0001-ai-interprets-code-decides.md
    title: AI interprets, code decides
---

# Purpose

A user describes a half-remembered book in plain text. The app returns real Open Library works, ordered candidates when uncertain, and a grounded reason for each. Topic requests return several fitting books, never a claimed single match.

# Boundary

AI may interpret the query and phrase explanations. Code retrieves and validates works, ranks them, decides status, and falls back cleanly when AI is unavailable. The API and React app form one deployable application. See [server](server.md), [frontend](frontend.md), and [AI](ai.md).
