# ADR 0007: Configuration and API keys

Status: accepted (build 2026-10-25)

## Context
Reviewers must run the app with one key (or none). Keys must never reach Git, logs, errors, fixtures or API responses.
The future frontend needs to show and change provider settings during local development.

## Decision
- Precedence (lowest → highest): `appsettings.json` → `appsettings.{Environment}.json` → user secrets (Development) →
  repo-root `.env` → environment variables (including the well-known `GEMINI_API_KEY`, `ANTHROPIC_API_KEY`,
  `OPENAI_API_KEY`) → command line → runtime admin overrides.
- The admin API (`/api/admin/ai…`) exists only in Development and only for loopback callers. Runtime changes live in
  memory and are lost on restart; the API never rewrites `.env` and never returns a key, only whether one is set, its
  source and a short SHA-256 fingerprint.

## Consequences
No database or secret store is needed. Production/containers use environment variables only.
