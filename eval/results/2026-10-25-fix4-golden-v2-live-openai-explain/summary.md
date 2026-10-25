# Eval: Find That Book golden set v2 · fix4-golden-v2-live-openai-explain

| Run | |
|---|---|
| Date | 2026-10-25 12:31 MT (-06:00) · 2026-10-25 14:31 ET (-04:00) |
| Code fingerprint (src/) | `sha256:882b8f88df395618 (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:f52acdfb227675cb (2 files)` |
| Eval tool fingerprint | `sha256:c6d8f205845ee93a (32 files)` |
| Dataset | Find That Book golden set v2 (`eval/golden.v2.json`, sha256:5b70cc75033b87c6) |
| Held out | no |
| Tuning | in_sample: Every row was run while the research rules were designed and tuned (research/05-08, the plan 04 fixes, the plan 06 v3 ablations replayed this set on cached live responses). in_hard_set marks the 30 queries that are also in the research hard set (research/07-fix-misses/hard.jsonl, 46 rows), which was in-sample too. Numbers on this set measure regressions and fidelity to the prototype, not generalization. |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | openai |
| Models (observed in traces) | explain: openai/gpt-4.1-mini<br>interpret: openai/gpt-4.1-mini |
| Interpretations | live LLM |
| Explanations | live LLM |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 60 cases · 60 run · 0 skipped · 1 rejected by validation · 0 errors · 60 in-sample |
| Concurrency / limit | 2 / 5 · LLM paced to 45 calls/min |
| Wall time | 144.9 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set golden --catalog live --provider openai --explain llm --max-llm-calls 130 --quiet --label fix4-golden-v2-live-openai-explain` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **47/47 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 57/57 = 1.000 | answerable rows, first shown candidate acceptable |
| Top-5 | 57/57 = 1.000 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 59/60 = .983 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 47/57 = .825 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 3 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| ambiguous | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 4/4 = 1.000 | 2 @ 1.000 | 0 |
| author_only | 5 | 5/5 = 1.000 | 5/5 = 1.000 | 5/5 = 1.000 | 0 @ n/a | 0 |
| brief | 6 | 6/6 = 1.000 | 6/6 = 1.000 | 5/6 = .833 | 4 @ 1.000 | 0 |
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
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/59 = .000
- Interpretation source: llm 59

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 59 | 2573 ms | 4031 ms | 6967 ms |
| Pipeline elapsed_ms | 59 | 2572 ms | 4031 ms | 6967 ms |
| stage `clean` | 59 | 0 ms | 0 ms | 4 ms |
| stage `explain` | 57 | 1463 ms | 1736 ms | 1943 ms |
| stage `interpret` | 59 | 872 ms | 1267 ms | 1383 ms |
| stage `judge` | 57 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 59 | 5 ms | 25 ms | 27 ms |
| stage `retrieve` | 59 | 1029 ms | 2860 ms | 5626 ms |
| Open Library call (not cache hits) | 83 | 559 ms | 1836 ms | 4836 ms |
| LLM `explain` (not cache hits) | 54 | 1467 ms | 1734 ms | 1942 ms |
| LLM `interpret` (not cache hits) | 59 | 872 ms | 1266 ms | 1383 ms |

## Open Library calls

- Logical calls per query: mean 1.49, max 5, at budget 1 (budget: 5 logical calls)
- Cache hits: 5/88 = .057
- HTTP attempts: 83 total, mean 1.41 per query; slow-empty re-queries 0
- Outcomes: ok 88
- Steps: broad 59, hypothesis 12, author_works 6, any_of 4, person 3, original_title 1, person_any 1, person_fallback 1, person_fallback_any 1

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 57 | 3 | ok 57 | 48611 | 8755 |
| interpret | 59 | 0 | ok 59 | 39477 | 3661 |

- Paid LLM calls (not cache hits, incl. judge): 113
- Estimated cost (IAiPricing, paid-tier list prices): $0.055101 for 59 searches = **$0.933912 per 1,000 searches**

## Misses (1)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| brief-06 | austen bennet | match · Pride and Prejudice | ambiguous (author_only), target at #1 | OL66554W Pride and Prejudice [D]; OL66513W Emma [D]; OL66562W Sense and Sensibility [D] |

## Prototype reference (not this build)

**Reference only: Python prototype numbers from the research phase, NOT measured by this build.** They were produced on golden v1 (none-04 scored as a 200 `none`; v2 expects a 400, same 60-row denominator) by the prototype's `golden_v3.py` replaying cached live responses from before v3, with LLM-on rows using cached gpt-4.1-mini prompt-v1 hypotheses (so the edition-title fix could not fire). Rows are in-sample: the rules were tuned on them.

| Prototype arm (source) | top-1 / top-5 (57) | status (60) | `match` n @ precision |
|---|---|---|---|
| prototype v3, LLM on | .982 / 1.000 | .983 | 45 @ 1.000 |
| prototype v3, LLM off | .982 / 1.000 | .933 | 39 @ 1.000 |
| prototype before v3 (first code fixes), LLM on, live | .947 / .982 | .983 | 44 @ 1.000 |
| prototype before v3 (first code fixes), LLM off, live | .912 / .982 | .950 | 39 @ .974 |
| raw Open Library order | 45/57 (.79) / 48/57 (.84) | 48/60 (.80) | 53 @ .74 |
| target set for the C# pipeline before it was built | ≥ .91 / ≥ .95 | ≥ .92 | precision ≥ .97 |

Noise: one golden query = 1.8 points of top-1.

Parse stability and retrieval variability are measured by the `stability` command (separate report).
