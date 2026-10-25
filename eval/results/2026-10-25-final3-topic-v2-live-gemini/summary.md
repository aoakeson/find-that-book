# Eval: topic requests and controls v2 · final3-topic-v2-live-gemini

> topic feature dev set: topic rows need the pipeline to expose matched subjects

| Run | |
|---|---|
| Date | 2026-10-25 03:19 MT (-06:00) · 2026-10-25 05:19 ET (-04:00) |
| Code fingerprint (src/) | `sha256:918d7b88cc5867cf (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:bb794a4ff02178f3 (2 files)` |
| Eval tool fingerprint | `sha256:9e8b27340734c970 (31 files)` |
| Dataset | topic requests and controls v2 (`eval/datasets/topic.v2.jsonl`, sha256:de6e58c6305a94e9) |
| Held out | no |
| Tuning | dev set for the topic-search feature (written before the feature exists); the feature will be built against it, so treat its numbers as in-sample |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | gemini |
| Models (observed in traces) | explain: gemini/gemini-3.5-flash-lite<br>interpret: gemini/gemini-3.5-flash-lite |
| Interpretations | live LLM |
| Explanations | live LLM |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 38 cases · 38 run · 0 skipped · 0 rejected by validation · 0 errors · 0 in-sample |
| Concurrency / limit | 2 / 5 · LLM paced to 11 calls/min |
| Wall time | 425.9 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set topic --catalog live --provider gemini --explain llm --max-llm-calls 80 --label final3-topic-v2-live-gemini` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **12/12 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 34/37 = .919 | answerable rows, first shown candidate acceptable |
| Top-5 | 34/37 = .919 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 35/38 = .921 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 12/37 = .324 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 1 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| topic | 24 | 21/24 = .875 | 21/24 = .875 | 21/24 = .875 | 0 @ n/a | 0 |
| topic_author | 1 | n/a | n/a | 1/1 = 1.000 | 0 @ n/a | 0 |
| topic_control | 13 | 13/13 = 1.000 | 13/13 = 1.000 | 13/13 = 1.000 | 12 @ 1.000 | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 120, template 62 (.659 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 13

## Degraded

- Degraded rate: 13/38 = .342
  - of which 2 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 1/38 = .026
- ai_budget_exhausted: 11
- explanation_fallback: 13
- Interpretation source: llm 27, heuristic 11

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 38 | 2805 ms | 5817 ms | 6284 ms |
| Pipeline elapsed_ms | 38 | 2805 ms | 5816 ms | 6282 ms |
| stage `clean` | 38 | 0 ms | 1 ms | 6 ms |
| stage `explain` | 37 | 1033 ms | 1612 ms | 2070 ms |
| stage `interpret` | 38 | 1030 ms | 1647 ms | 1950 ms |
| stage `judge` | 37 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 38 | 19 ms | 43 ms | 97 ms |
| stage `retrieve` | 38 | 1872 ms | 4621 ms | 5253 ms |
| Open Library call (not cache hits) | 85 | 667 ms | 3330 ms | 3575 ms |
| LLM `explain` (not cache hits) | 37 | 1031 ms | 1610 ms | 2067 ms |
| LLM `interpret` (not cache hits) | 38 | 1030 ms | 1647 ms | 1950 ms |

## Open Library calls

- Logical calls per query: mean 2.29, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 2/87 = .023
- HTTP attempts: 85 total, mean 2.24 per query; slow-empty re-queries 0
- Outcomes: ok 87
- Steps: broad 38, topic 23, suggestions 18, any_of 3, person_fallback 2, person_fallback_any 2, person 1

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 37 | 0 | ok 24, budget_exhausted 12, rate_limited 1 | 21049 (13 unreported) | 5749 (13 unreported) |
| interpret | 38 | 0 | ok 27, budget_exhausted 11 | 14184 (11 unreported) | 4902 (11 unreported) |

- Paid LLM calls (not cache hits, incl. judge): 52
- Estimated cost (IAiPricing, paid-tier list prices): $0.037197 for 38 searches = **$0.978879 per 1,000 searches**

## Misses (3)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| topic-12 | a mystery set in venice | ambiguous · topic: venice, venezia | none (weak_evidence) |  |
| topic-18 | a cookbook for italian food | ambiguous · topic: cooking, cookery | ambiguous (near_title_author) | OL25072868W Quick from scratch [C]; OL29312082W Quick from Scratch Italian Cookbook [C]; OL19984365W Good housekeeping family Italian cookbo… [C] |
| topic-20 | a biography of abraham lincoln | ambiguous · topic: lincoln | ambiguous (weak_evidence) | OL1164736W Abraham Lincoln [E]; OL2710351W Abraham Lincoln [E]; OL3920712W Acres of diamonds [E] |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
