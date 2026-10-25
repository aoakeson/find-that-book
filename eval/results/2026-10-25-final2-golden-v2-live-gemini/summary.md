# Eval: Find That Book golden set v2 · final2-golden-v2-live-gemini

| Run | |
|---|---|
| Date | 2026-10-25 02:50 MT (-06:00) · 2026-10-25 04:50 ET (-04:00) |
| Code fingerprint (src/) | `sha256:ebda00bcfff6976b (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:81564537deb86b5e (2 files)` |
| Eval tool fingerprint | `sha256:d8599dc7e0dac341 (31 files)` |
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
| Wall time | 667.5 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set golden --catalog live --provider gemini --explain llm --max-llm-calls 130 --label final2-golden-v2-live-gemini` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **48/48 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 56/57 = .982 | answerable rows, first shown candidate acceptable |
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
| ambiguous | 4 | 3/4 = .750 | 4/4 = 1.000 | 4/4 = 1.000 | 2 @ 1.000 | 0 |
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

- Source mix (shown candidates): llm 287, template 2 (.993 llm)
- Grounding rejections (template shown although the explain call succeeded): 2
- Rows with explanation_fallback (explain call failed/unavailable): 0

## Degraded

- Degraded rate: 0/59 = .000
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/59 = .000
- Interpretation source: llm 59

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 59 | 2295 ms | 4611 ms | 7506 ms |
| Pipeline elapsed_ms | 59 | 2295 ms | 4586 ms | 7506 ms |
| stage `clean` | 59 | 0 ms | 0 ms | 4 ms |
| stage `explain` | 58 | 1031 ms | 1583 ms | 2971 ms |
| stage `interpret` | 59 | 935 ms | 1743 ms | 2669 ms |
| stage `judge` | 58 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 59 | 10 ms | 32 ms | 48 ms |
| stage `retrieve` | 59 | 1168 ms | 3670 ms | 6137 ms |
| Open Library call (not cache hits) | 80 | 560 ms | 2751 ms | 4813 ms |
| LLM `explain` (not cache hits) | 55 | 1092 ms | 1582 ms | 2969 ms |
| LLM `interpret` (not cache hits) | 59 | 935 ms | 1743 ms | 2669 ms |

## Open Library calls

- Logical calls per query: mean 1.46, max 3, at budget 0 (budget: 5 logical calls)
- Cache hits: 6/86 = .070
- HTTP attempts: 80 total, mean 1.36 per query; slow-empty re-queries 0
- Outcomes: ok 86
- Steps: broad 59, hypothesis 12, topic 6, suggestions 5, author_works 2, any_of 1, original_title 1

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 58 | 3 | ok 58 | 47894 | 11515 |
| interpret | 59 | 0 | ok 59 | 22842 | 6549 |

- Paid LLM calls (not cache hits, incl. judge): 114
- Estimated cost (IAiPricing, paid-tier list prices): $0.066381 for 59 searches = **$1.125098 per 1,000 searches**

## Misses (1)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| ambig-03 | hermione | ambiguous/none · author: J. K. Rowling | ambiguous (weak_evidence), target at #3 | OL52818W HERmione [A]; OL8136902W Hermione [A]; OL82563W Harry Potter and the Philosopher's Stone [E] |

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
