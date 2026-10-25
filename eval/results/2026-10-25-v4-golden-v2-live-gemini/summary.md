# Eval: Find That Book golden set v2 · v4-golden-v2-live-gemini

> **Partial run:** provider errors on 5 of 10 searched rows (> 20%): stopped; wait, then re-run the missing rows with --resume <this folder>. 49 rows were not run and are not scored.

| Run | |
|---|---|
| Date | 2026-10-25 02:32 MT (-06:00) · 2026-10-25 04:32 ET (-04:00) |
| Code fingerprint (src/) | `sha256:b872851edf7c3974 (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:c584f6687a180097 (2 files)` |
| Eval tool fingerprint | `sha256:1caf4d1d3f242957 (31 files)` |
| Dataset | Find That Book golden set v2 (`eval/golden.v2.json`, sha256:5b70cc75033b87c6) |
| Held out | no |
| Tuning | in_sample: Every row was run while the research rules were designed and tuned (research/05-08, the plan 04 fixes, the plan 06 v3 ablations replayed this set on cached live responses). in_hard_set marks the 30 queries that are also in the research hard set (research/07-fix-misses/hard.jsonl, 46 rows), which was in-sample too. Numbers on this set measure regressions and fidelity to the prototype, not generalization. |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | gemini |
| Models (observed in traces) | explain: gemini/gemini-3.5-flash-lite<br>interpret: gemini/gemini-3.5-flash-lite |
| Interpretations | live LLM |
| Explanations | live LLM |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 60 cases · 11 run · 49 skipped · 0 rejected by validation · 0 errors · 60 in-sample |
| Concurrency / limit | 2 / 5 · LLM paced to 11 calls/min |
| Wall time | 128.3 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set golden --catalog live --provider gemini --explain llm --max-llm-calls 130 --label v4-golden-v2-live-gemini` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **9/9 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 10/11 = .909 | answerable rows, first shown candidate acceptable |
| Top-5 | 11/11 = 1.000 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 10/11 = .909 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 9/11 = .818 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 0 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| ambiguous | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| author_only | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| brief | 6 | 5/6 = .833 | 6/6 = 1.000 | 5/6 = .833 | 4 @ 1.000 | 0 |
| character | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| contributor | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| diacritics | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| duplicates | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| noisy | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| none | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| partial | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| subtitle | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| title_author | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |
| title_only | 5 | 5/5 = 1.000 | 5/5 = 1.000 | 5/5 = 1.000 | 5 @ 1.000 | 0 |
| typo | 0 | n/a | n/a | n/a | 0 @ n/a | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 45, template 10 (.818 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 2

## Degraded

- Degraded rate: 6/11 = .545
  - of which 1 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 6/11 = .545
- interpretation_fallback: 5
- explanation_fallback: 2
- Interpretation source: llm 6, heuristic 5

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 11 | 7988 ms | 9542 ms | 9542 ms |
| Pipeline elapsed_ms | 11 | 7988 ms | 9542 ms | 9542 ms |
| stage `clean` | 11 | 0 ms | 5 ms | 5 ms |
| stage `explain` | 11 | 4004 ms | 5007 ms | 5007 ms |
| stage `interpret` | 11 | 2978 ms | 4010 ms | 4010 ms |
| stage `judge` | 11 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 11 | 9 ms | 50 ms | 50 ms |
| stage `retrieve` | 11 | 2981 ms | 5047 ms | 5047 ms |
| Open Library call (not cache hits) | 17 | 609 ms | 2340 ms | 2340 ms |
| LLM `explain` (not cache hits) | 11 | 4002 ms | 5002 ms | 5002 ms |
| LLM `interpret` (not cache hits) | 11 | 2978 ms | 4006 ms | 4006 ms |

## Open Library calls

- Logical calls per query: mean 1.55, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 0/17 = .000
- HTTP attempts: 17 total, mean 1.55 per query; slow-empty re-queries 0
- Outcomes: ok 17
- Steps: broad 11, any_of 1, author_works 1, hypothesis 1, person_fallback 1, suggestions 1, topic 1

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 11 | 0 | ok 9, timeout 2 | 7619 (2 unreported) | 1743 (2 unreported) |
| interpret | 11 | 0 | ok 6, timeout 5 | 2320 (5 unreported) | 675 (5 unreported) |

- Paid LLM calls (not cache hits, incl. judge): 22
- Estimated cost (IAiPricing, paid-tier list prices): $0.009027 for 11 searches = **$0.820609 per 1,000 searches** (lower bound: 7 calls unpriced)

## Misses (2)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| brief-04 | dickens | ambiguous · author: Charles Dickens | ambiguous (weak_evidence), target at #2 | OL47997W Dickens [E]; OL32466W A Christmas Carol [D]; OL8193465W A Tale of Two Cities [D] |
| brief-06 | austen bennet | match · Pride and Prejudice | ambiguous (author_only), target at #1 | OL66554W Pride and Prejudice [D]; OL66513W Emma [D]; OL66562W Sense and Sensibility [D] |

## Prototype reference (not this build)

**Reference only: Python prototype numbers from the research phase, NOT measured by this build.** They were produced on golden v1 (none-04 scored as a 200 `none`; v2 expects a 400, same 60-row denominator) by research/12 `golden_v3.py` replaying research/07's cached live responses, with LLM-on rows using cached gpt-4.1-mini prompt-v1 hypotheses (so plan 06 fix 6 could not fire). Rows are in-sample: the rules were tuned on them.

| Prototype arm (source) | top-1 / top-5 (57) | status (60) | `match` n @ precision |
|---|---|---|---|
| v3, LLM on (plan 06) | .982 / 1.000 | .983 | 45 @ 1.000 |
| v3, LLM off (plan 06) | .982 / 1.000 | .933 | 39 @ 1.000 |
| 07 code fixes, LLM on, live (plan 04) | .947 / .982 | .983 | 44 @ 1.000 |
| 07 code fixes, LLM off, live (plan 04) | .912 / .982 | .950 | 39 @ .974 |
| raw Open Library order (plan 03 arm A) | 45/57 (.79) / 48/57 (.84) | 48/60 (.80) | 53 @ .74 |
| Target set for the C# pipeline (plan 03) | ≥ .91 / ≥ .95 | ≥ .92 | precision ≥ .97 |

Noise: one golden query = 1.8 points of top-1.

Parse stability and retrieval variability are measured by the `stability` command (separate report).
