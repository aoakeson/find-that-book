# Eval: Find That Book golden set v2 · golden-v2-live-gemini

| Run | |
|---|---|
| Date | 2026-10-25 01:16 MT (-06:00) · 2026-10-25 03:16 ET (-04:00) |
| Code fingerprint (src/) | `sha256:04b946a70ca01b03 (145 files)` |
| Eval tool fingerprint | `sha256:c78ce8ea8ab91360 (31 files)` |
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
| Counts | 60 cases · 60 run · 0 skipped · 1 rejected by validation · 0 errors · 60 in-sample |
| Concurrency / limit | 2 / 5 · LLM paced to 11 calls/min |
| Wall time | 668.0 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set golden --catalog live --provider gemini --explain llm --max-llm-calls 130 --label golden-v2-live-gemini` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **48/48 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 57/57 = 1.000 | answerable rows, first shown candidate acceptable |
| Top-5 | 57/57 = 1.000 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 60/60 = 1.000 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 48/57 = .842 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 3 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| ambiguous | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 4/4 = 1.000 | 2 @ 1.000 | 0 |
| author_only | 5 | 5/5 = 1.000 | 5/5 = 1.000 | 5/5 = 1.000 | 0 @ n/a | 0 |
| brief | 6 | 6/6 = 1.000 | 6/6 = 1.000 | 6/6 = 1.000 | 5 @ 1.000 | 0 |
| character | 5 | 5/5 = 1.000 | 5/5 = 1.000 | 5/5 = 1.000 | 5 @ 1.000 | 0 |
| contributor | 3 | 3/3 = 1.000 | 3/3 = 1.000 | 3/3 = 1.000 | 3 @ 1.000 | 0 |
| diacritics | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 4/4 = 1.000 | 4 @ 1.000 | 0 |
| duplicates | 3 | 3/3 = 1.000 | 3/3 = 1.000 | 3/3 = 1.000 | 3 @ 1.000 | 0 |
| noisy | 5 | 5/5 = 1.000 | 5/5 = 1.000 | 5/5 = 1.000 | 5 @ 1.000 | 0 |
| none | 3 | n/a | n/a | 3/3 = 1.000 | 0 @ n/a | 0 |
| partial | 3 | 3/3 = 1.000 | 3/3 = 1.000 | 3/3 = 1.000 | 3 @ 1.000 | 0 |
| subtitle | 3 | 3/3 = 1.000 | 3/3 = 1.000 | 3/3 = 1.000 | 2 @ 1.000 | 0 |
| title_author | 6 | 6/6 = 1.000 | 6/6 = 1.000 | 6/6 = 1.000 | 6 @ 1.000 | 0 |
| title_only | 5 | 5/5 = 1.000 | 5/5 = 1.000 | 5/5 = 1.000 | 5 @ 1.000 | 0 |
| typo | 5 | 5/5 = 1.000 | 5/5 = 1.000 | 5/5 = 1.000 | 5 @ 1.000 | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 284, template 0 (1.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 0

## Degraded

- Degraded rate: 0/59 = .000
- Interpretation source: llm 59

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 59 | 2118 ms | 5008 ms | 10799 ms |
| Pipeline elapsed_ms | 59 | 2118 ms | 5007 ms | 10799 ms |
| stage `clean` | 59 | 0 ms | 0 ms | 2 ms |
| stage `explain` | 57 | 1033 ms | 3565 ms | 4809 ms |
| stage `interpret` | 59 | 831 ms | 1599 ms | 2696 ms |
| stage `judge` | 57 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 59 | 5 ms | 31 ms | 63 ms |
| stage `retrieve` | 59 | 944 ms | 3196 ms | 9892 ms |
| Open Library call (not cache hits) | 78 | 534 ms | 1918 ms | 8746 ms |
| LLM `explain` (not cache hits) | 54 | 1042 ms | 3564 ms | 4806 ms |
| LLM `interpret` (not cache hits) | 59 | 831 ms | 1599 ms | 2696 ms |

## Open Library calls

- Logical calls per query: mean 1.42, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 6/84 = .071
- HTTP attempts: 79 total, mean 1.34 per query; slow-empty re-queries 0
- Outcomes: ok 84
- Steps: broad 59, hypothesis 12, author_works 6, any_of 3, original_title 1, person 1, person_fallback 1, person_fallback_any 1

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 57 | 3 | ok 57 | 43361 | 11528 |
| interpret | 59 | 0 | ok 59 | 14110 | 4303 |

- Paid LLM calls (not cache hits, incl. judge): 113
- Estimated cost (IAiPricing, paid-tier list prices): $0.056819 for 59 searches = **$0.963031 per 1,000 searches**

## Misses (0)

None.

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
