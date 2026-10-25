# ADR 0001: AI interprets, code decides

Status: accepted (2026-10-24, build 2026-10-25)

## Context
A query like "tolkien hobbit illustrated deluxe 1937" mixes author, title and noise. An LLM is good at reading intent
("mark huckleberry" means Mark Twain's Huckleberry Finn) but it can name books that don't exist and it does not know which
of 56 Open Library records titled "Pride and Prejudice" is the canonical one. The interview asked how to trust a
confidence number that says 78 one run and 85 the next.

## Decision
- The LLM produces a *search hypothesis* (title, author, characters, keywords, English and original titles) and, from
  validated facts only, the "why this book" sentence.
- Deterministic C# in `FindThatBook.Core` resolves primary authors, deduplicates works, assigns the brief's tiers A–E,
  scores and orders candidates, and decides the status (`match` / `ambiguous` / `none`).
- A book is returned only if Open Library returned it. Explanations are checked against the facts and replaced by a
  template sentence when they add anything unsupported.
- With the LLM off or failing, the same pipeline runs without a hypothesis (`degraded`), never an error.

## Consequences
- Ranking is reproducible: identical query, hypothesis and candidates give identical output, with stable tie-breaks.
- Interpretation variability (the LLM) and retrieval variability (Open Library) are measured separately by the eval tool.
- We give up LLM re-ranking. Measured in research: an LLM ranker was ±2 points (noise) and broke the brief's
  "hobbit dixon" tier-B case; the optional judge (ADR 0004) recovers its useful part without reordering.
