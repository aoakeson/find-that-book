# Eval: topic requests and controls v2 · final3-topic-v2-live-none

> topic feature dev set: topic rows need the pipeline to expose matched subjects

| Run | |
|---|---|
| Date | 2026-10-25 03:27 MT (-06:00) · 2026-10-25 05:27 ET (-04:00) |
| Code fingerprint (src/) | `sha256:918d7b88cc5867cf (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:bb794a4ff02178f3 (2 files)` |
| Eval tool fingerprint | `sha256:9e8b27340734c970 (31 files)` |
| Dataset | topic requests and controls v2 (`eval/datasets/topic.v2.jsonl`, sha256:de6e58c6305a94e9) |
| Held out | no |
| Tuning | dev set for the topic-search feature (written before the feature exists); the feature will be built against it, so treat its numbers as in-sample |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | none |
| Models (observed in traces) | none (no LLM call reached a provider) |
| Interpretations | none (LLM off) |
| Explanations | template (LLM off) |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 38 cases · 38 run · 0 skipped · 0 rejected by validation · 0 errors · 0 in-sample |
| Concurrency / limit | 2 / 5 |
| Wall time | 28.3 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set topic --catalog live --provider none --quiet --label final3-topic-v2-live-none` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **12/12 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 31/37 = .838 | answerable rows, first shown candidate acceptable |
| Top-5 | 30/37 = .811 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 32/38 = .842 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 12/37 = .324 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 1 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| topic | 24 | 18/24 = .750 | 17/24 = .708 | 18/24 = .750 | 0 @ n/a | 0 |
| topic_author | 1 | n/a | n/a | 1/1 = 1.000 | 0 @ n/a | 0 |
| topic_control | 13 | 13/13 = 1.000 | 13/13 = 1.000 | 13/13 = 1.000 | 12 @ 1.000 | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 0, template 168 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 35

## Degraded

- Degraded rate: 38/38 = 1.000
  - of which 38 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/38 = .000
- ai_not_configured: 38
- explanation_fallback: 35
- Interpretation source: heuristic 38

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 38 | 843 ms | 4786 ms | 6617 ms |
| Pipeline elapsed_ms | 38 | 843 ms | 4786 ms | 6617 ms |
| stage `clean` | 38 | 0 ms | 0 ms | 6 ms |
| stage `explain` | 35 | 0 ms | 2 ms | 10 ms |
| stage `interpret` | 38 | 0 ms | 0 ms | 7 ms |
| stage `judge` | 35 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 38 | 12 ms | 62 ms | 89 ms |
| stage `retrieve` | 38 | 842 ms | 4785 ms | 6617 ms |
| Open Library call (not cache hits) | 72 | 649 ms | 2927 ms | 5186 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 0 | n/a | n/a | n/a |

## Open Library calls

- Logical calls per query: mean 1.95, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 2/74 = .027
- HTTP attempts: 72 total, mean 1.89 per query; slow-empty re-queries 0
- Outcomes: ok 74
- Steps: broad 38, topic 20, any_of 6, person_fallback 5, person_fallback_any 5

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 35 | 0 | not_configured 35 | n/a | n/a |
| interpret | 38 | 0 | not_configured 38 | n/a | n/a |

- Paid LLM calls (not cache hits, incl. judge): 0
- Estimated cost: n/a (no priced calls)

## Misses (6)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| topic-06 | un livre sur les ours | ambiguous · topic: ours, bear | none (weak_evidence) |  |
| topic-07 | books like harry potter | ambiguous · topic: magic, wizard, witch, fantasy | ambiguous (weak_evidence) | OL82563W Harry Potter and the Philosopher's Stone [E]; OL82537W Harry Potter and the Chamber of Secrets [E]; OL82536W Harry Potter and the Prisoner of Azkaban [E] |
| topic-12 | a mystery set in venice | ambiguous · topic: venice, venezia | none (weak_evidence) |  |
| topic-17 | teach me about volcanoes | ambiguous · topic: volcan* | none (weak_evidence) |  |
| topic-18 | a cookbook for italian food | ambiguous · topic: cooking, cookery | ambiguous (near_title_author) | OL25072868W Quick from scratch [C]; OL29312082W Quick from Scratch Italian Cookbook [C]; OL19984365W Good housekeeping family Italian cookbo… [C] |
| topic-20 | a biography of abraham lincoln | ambiguous · topic: lincoln | ambiguous (weak_evidence) | OL1164736W Abraham Lincoln [E]; OL2710351W Abraham Lincoln [E]; OL547941W Abraham Lincoln [E] |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
