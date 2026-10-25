# Eval: topic requests and controls v2 · final3-topic-v2-live-openai-interpret

> topic feature dev set: topic rows need the pipeline to expose matched subjects

| Run | |
|---|---|
| Date | 2026-10-25 04:04 MT (-06:00) · 2026-10-25 06:04 ET (-04:00) |
| Code fingerprint (src/) | `sha256:918d7b88cc5867cf (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:bb794a4ff02178f3 (2 files)` |
| Eval tool fingerprint | `sha256:bfa0664db82124b7 (31 files)` |
| Dataset | topic requests and controls v2 (`eval/datasets/topic.v2.jsonl`, sha256:de6e58c6305a94e9) |
| Held out | no |
| Tuning | dev set for the topic-search feature (written before the feature exists); the feature will be built against it, so treat its numbers as in-sample |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | openai |
| Models (observed in traces) | interpret: openai/gpt-4.1-mini |
| Interpretations | live LLM |
| Explanations | template only (--explain template: the explain LLM is never called) |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 38 cases · 38 run · 0 skipped · 0 rejected by validation · 0 errors · 0 in-sample |
| Concurrency / limit | 2 / 5 · LLM paced to 45 calls/min |
| Wall time | 34.3 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set topic --catalog live --provider openai --explain template --max-llm-calls 50 --quiet --label final3-topic-v2-live-openai-interpret` |

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

- Source mix (shown candidates): llm 0, template 185 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 37

## Degraded

- Degraded rate: 37/38 = .974
  - of which 37 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/38 = .000
- explanation_fallback: 37
- Interpretation source: llm 38

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 38 | 1863 ms | 3159 ms | 3377 ms |
| Pipeline elapsed_ms | 38 | 1863 ms | 3159 ms | 3377 ms |
| stage `clean` | 38 | 0 ms | 0 ms | 6 ms |
| stage `explain` | 37 | 0 ms | 3 ms | 8 ms |
| stage `interpret` | 38 | 1131 ms | 1406 ms | 1452 ms |
| stage `judge` | 37 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 38 | 18 ms | 48 ms | 72 ms |
| stage `retrieve` | 38 | 1861 ms | 3159 ms | 3376 ms |
| Open Library call (not cache hits) | 84 | 726 ms | 1633 ms | 1843 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 38 | 1130 ms | 1406 ms | 1451 ms |

## Open Library calls

- Logical calls per query: mean 2.37, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 6/90 = .067
- HTTP attempts: 84 total, mean 2.21 per query; slow-empty re-queries 0
- Outcomes: ok 90
- Steps: broad 38, suggestions 22, topic 22, any_of 3, person_fallback 2, person_fallback_any 2, person 1

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 37 | 0 | not_configured 37 | n/a | n/a |
| interpret | 38 | 0 | ok 38 | 25435 | 3836 |

- Paid LLM calls (not cache hits, incl. judge): 38
- Estimated cost (IAiPricing, paid-tier list prices): $0.016312 for 38 searches = **$0.429253 per 1,000 searches**

## Misses (3)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| topic-12 | a mystery set in venice | ambiguous · topic: venice, venezia | none (weak_evidence) |  |
| topic-18 | a cookbook for italian food | ambiguous · topic: cooking, cookery | ambiguous (near_title_author) | OL25072868W Quick from scratch [C]; OL29312082W Quick from Scratch Italian Cookbook [C]; OL19984365W Good housekeeping family Italian cookbo… [C] |
| topic-20 | a biography of abraham lincoln | ambiguous · topic: lincoln | ambiguous (weak_evidence) | OL1164736W Abraham Lincoln [E]; OL2710351W Abraham Lincoln [E]; OL3920712W Acres of diamonds [E] |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
