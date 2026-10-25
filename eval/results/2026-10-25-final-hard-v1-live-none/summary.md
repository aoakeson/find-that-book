# Eval: research hard set v1 · final-hard-v1-live-none

| Run | |
|---|---|
| Date | 2026-10-25 01:30 MT (-06:00) · 2026-10-25 03:30 ET (-04:00) |
| Code fingerprint (src/) | `sha256:dd58f98dffa8042f (145 files)` |
| Eval tool fingerprint | `sha256:cb20c194ac840ad9 (31 files)` |
| Dataset | research hard set v1 (`eval/datasets/hard.v1.jsonl`, sha256:2d6daa40170ce0e6) |
| Held out | no |
| Tuning | in_sample: hand-labelled hard queries the research rules (plan 04 fixes, plan 06 v3) were designed and tuned on; 30 queries overlap golden. Measures regressions/fidelity to the prototype, not generalization. |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | none |
| Models (observed in traces) | none (no LLM call reached a provider) |
| Interpretations | none (LLM off) |
| Explanations | template (LLM off) |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 46 cases · 46 run · 0 skipped · 0 rejected by validation · 0 errors · 46 in-sample |
| Concurrency / limit | 2 / 5 |
| Wall time | 30.9 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set hard --catalog live --provider none --quiet --label final-hard-v1-live-none` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **24/24 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 43/46 = .935 | answerable rows, first shown candidate acceptable |
| Top-5 | 43/46 = .935 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | n/a | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 24/46 = .522 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 0 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| abbrev | 1 | 0/1 = .000 | 0/1 = .000 | n/a | 0 @ n/a | 0 |
| apostrophe | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |
| author_only | 4 | 4/4 = 1.000 | 4/4 = 1.000 | n/a | 0 @ n/a | 0 |
| brief | 4 | 4/4 = 1.000 | 4/4 = 1.000 | n/a | 3 @ 1.000 | 0 |
| character | 14 | 12/14 = .857 | 12/14 = .857 | n/a | 0 @ n/a | 0 |
| contributor | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |
| diacritics | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |
| duplicates | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| noisy | 3 | 3/3 = 1.000 | 3/3 = 1.000 | n/a | 3 @ 1.000 | 0 |
| number_title | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| partial | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |
| study_guide | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 0 @ n/a | 0 |
| subtitle | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| title_author | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| title_only | 5 | 5/5 = 1.000 | 5/5 = 1.000 | n/a | 4 @ 1.000 | 0 |
| typo | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 0, template 223 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 45

## Degraded

- Degraded rate: 46/46 = 1.000
  - of which 46 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/46 = .000
- ai_not_configured: 46
- explanation_fallback: 45
- Interpretation source: heuristic 46

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 46 | 782 ms | 2918 ms | 7314 ms |
| Pipeline elapsed_ms | 46 | 782 ms | 2918 ms | 7314 ms |
| stage `clean` | 46 | 0 ms | 0 ms | 2 ms |
| stage `explain` | 45 | 0 ms | 3 ms | 10 ms |
| stage `interpret` | 46 | 0 ms | 0 ms | 17 ms |
| stage `judge` | 45 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 46 | 6 ms | 34 ms | 55 ms |
| stage `retrieve` | 46 | 782 ms | 2918 ms | 7313 ms |
| Open Library call (not cache hits) | 78 | 538 ms | 1860 ms | 7309 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 0 | n/a | n/a | n/a |

## Open Library calls

- Logical calls per query: mean 1.72, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 1/79 = .013
- HTTP attempts: 78 total, mean 1.70 per query; slow-empty re-queries 0
- Outcomes: ok 79
- Steps: broad 46, any_of 13, person_fallback 12, author_works 4, person_fallback_any 4

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 45 | 0 | not_configured 45 | n/a | n/a |
| interpret | 46 | 0 | not_configured 46 | n/a | n/a |

- Paid LLM calls (not cache hits, incl. judge): 0
- Estimated cost: n/a (no priced calls)

## Misses (3)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| hard.v1-0014 | holden caulfield |  · The Catcher in the Rye / J. D. Salinger | none (weak_evidence) |  |
| hard.v1-0016 | jean valjean |  · Les Misérables / Victor Hugo | ambiguous (close_scores) | OL24185477W Jean Valjean [A]; OL11447462W Jean Valjean, or, The shadow of the law [A]; OL27717901W Les misérables. Jean Valjean [2/2] [E] |
| hard.v1-0035 | lotr |  · The Lord of the Rings / J.R.R. Tolkien | ambiguous (weak_evidence) | OL17846747W Rogue One - A Star Wars Story [E]; OL27359995W Lord of the Rings Tarot Deck and Book S… [A]; OL28769920W Łotr [A] |

## Prototype reference (not this build)

**Reference only: Python prototype numbers from the research phase, NOT measured by this build.** They were produced on golden v1 (none-04 scored as a 200 `none`; v2 expects a 400, same 60-row denominator) by research/12 `golden_v3.py` replaying research/07's cached live responses, with LLM-on rows using cached gpt-4.1-mini prompt-v1 hypotheses (so plan 06 fix 6 could not fire). Rows are in-sample: the rules were tuned on them.

| Prototype arm (plan 06) | top-1 / top-5 (46) | coverage @ precision |
|---|---|---|
| v3, LLM on | 1.000 / 1.000 | .804 @ 1.000 |
| v3, LLM off | .957 / .957 | .522 @ .958 |

Noise: one hard-set query = 2.2 points.

Parse stability and retrieval variability are measured by the `stability` command (separate report).
