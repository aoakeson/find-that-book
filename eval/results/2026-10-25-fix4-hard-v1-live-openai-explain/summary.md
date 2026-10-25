# Eval: research hard set v1 · fix4-hard-v1-live-openai-explain

| Run | |
|---|---|
| Date | 2026-10-25 12:34 MT (-06:00) · 2026-10-25 14:34 ET (-04:00) |
| Code fingerprint (src/) | `sha256:882b8f88df395618 (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:f52acdfb227675cb (2 files)` |
| Eval tool fingerprint | `sha256:c6d8f205845ee93a (32 files)` |
| Dataset | research hard set v1 (`eval/datasets/hard.v1.jsonl`, sha256:2d6daa40170ce0e6) |
| Held out | no |
| Tuning | in_sample: hand-labelled hard queries the research rules (plan 04 fixes, plan 06 v3) were designed and tuned on; 30 queries overlap golden. Measures regressions/fidelity to the prototype, not generalization. |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | openai |
| Models (observed in traces) | explain: openai/gpt-4.1-mini<br>interpret: openai/gpt-4.1-mini |
| Interpretations | live LLM |
| Explanations | live LLM |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 46 cases · 46 run · 0 skipped · 0 rejected by validation · 0 errors · 46 in-sample |
| Concurrency / limit | 2 / 5 · LLM paced to 45 calls/min |
| Wall time | 122.4 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set hard --catalog live --provider openai --explain llm --max-llm-calls 100 --quiet --label fix4-hard-v1-live-openai-explain` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **38/38 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 46/46 = 1.000 | answerable rows, first shown candidate acceptable |
| Top-5 | 46/46 = 1.000 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | n/a | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 38/46 = .826 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 0 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| abbrev | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| apostrophe | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |
| author_only | 4 | 4/4 = 1.000 | 4/4 = 1.000 | n/a | 0 @ n/a | 0 |
| brief | 4 | 4/4 = 1.000 | 4/4 = 1.000 | n/a | 4 @ 1.000 | 0 |
| character | 14 | 14/14 = 1.000 | 14/14 = 1.000 | n/a | 11 @ 1.000 | 0 |
| contributor | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |
| diacritics | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |
| duplicates | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| noisy | 3 | 3/3 = 1.000 | 3/3 = 1.000 | n/a | 3 @ 1.000 | 0 |
| number_title | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| partial | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |
| study_guide | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 0 @ n/a | 0 |
| subtitle | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| title_author | 1 | 1/1 = 1.000 | 1/1 = 1.000 | n/a | 1 @ 1.000 | 0 |
| title_only | 5 | 5/5 = 1.000 | 5/5 = 1.000 | n/a | 5 @ 1.000 | 0 |
| typo | 2 | 2/2 = 1.000 | 2/2 = 1.000 | n/a | 2 @ 1.000 | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 230, template 0 (1.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 0

## Degraded

- Degraded rate: 0/46 = .000
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/46 = .000
- Interpretation source: llm 46

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 46 | 2646 ms | 4200 ms | 6095 ms |
| Pipeline elapsed_ms | 46 | 2646 ms | 4200 ms | 6095 ms |
| stage `clean` | 46 | 0 ms | 0 ms | 4 ms |
| stage `explain` | 46 | 1479 ms | 1824 ms | 1915 ms |
| stage `interpret` | 46 | 824 ms | 1045 ms | 1126 ms |
| stage `judge` | 46 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 46 | 5 ms | 18 ms | 28 ms |
| stage `retrieve` | 46 | 1103 ms | 2885 ms | 4838 ms |
| Open Library call (not cache hits) | 71 | 523 ms | 2203 ms | 4830 ms |
| LLM `explain` (not cache hits) | 45 | 1479 ms | 1823 ms | 1912 ms |
| LLM `interpret` (not cache hits) | 46 | 824 ms | 1039 ms | 1126 ms |

## Open Library calls

- Logical calls per query: mean 1.57, max 3, at budget 0 (budget: 5 logical calls)
- Cache hits: 1/72 = .014
- HTTP attempts: 71 total, mean 1.54 per query; slow-empty re-queries 0
- Outcomes: ok 72
- Steps: broad 46, hypothesis 19, author_works 4, person 2, any_of 1

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 46 | 1 | ok 46 | 40766 | 7313 |
| interpret | 46 | 0 | ok 46 | 30754 | 2781 |

- Paid LLM calls (not cache hits, incl. judge): 91
- Estimated cost (IAiPricing, paid-tier list prices): $0.044758 for 46 searches = **$0.973009 per 1,000 searches**

## Misses (0)

None.

## Prototype reference (not this build)

**Reference only: Python prototype numbers from the research phase, NOT measured by this build.** They were produced on golden v1 (none-04 scored as a 200 `none`; v2 expects a 400, same 60-row denominator) by the prototype's `golden_v3.py` replaying cached live responses from before v3, with LLM-on rows using cached gpt-4.1-mini prompt-v1 hypotheses (so the edition-title fix could not fire). Rows are in-sample: the rules were tuned on them.

| Prototype arm | top-1 / top-5 (46) | coverage @ precision |
|---|---|---|
| prototype v3, LLM on | 1.000 / 1.000 | .804 @ 1.000 |
| prototype v3, LLM off | .957 / .957 | .522 @ .958 |

Noise: one hard-set query = 2.2 points.

Parse stability and retrieval variability are measured by the `stability` command (separate report).
