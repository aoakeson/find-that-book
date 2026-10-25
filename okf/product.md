---
type: Product
title: Find That Book
description: Find an Open Library work from a messy book description without overstating certainty.
tags: [app, search]
status: stable
generated: { by: cursor/claude-opus-5.5, at: 2026-10-25T22:40:00Z }
sources:
  - resource: ../README.md
    title: Project overview, behavior, and live demo
  - resource: ../docs/adr/0001-ai-interprets-code-decides.md
    title: AI interprets, code decides
  - resource: ../docs/DEPLOYMENT.md
    title: Fly.io release and verification
  - resource: ../fly.toml
    title: Hosted demo configuration
---

# Purpose

A user describes a half-remembered book in plain text. The app returns real Open Library works, ordered candidates when uncertain, and a grounded reason for each. Topic requests return several fitting books, never a claimed single match.

# Boundary

AI may interpret the query and phrase explanations. Code retrieves and validates works, ranks them, decides status, and falls back cleanly when AI is unavailable. The API and React app form one deployable application. See [server](server.md), [frontend](frontend.md), and [AI](ai.md).

# Deployment

The live demo is <https://books.andrewoakeson.com/>, the repo's Docker image on Fly.io in Production, so admin routes are off. Pushes to `main` deploy after CI passes. Point readers to `/how-it-works` for the illustrated walkthrough.
