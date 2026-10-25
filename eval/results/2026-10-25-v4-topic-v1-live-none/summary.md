# Eval: topic requests and controls v1 · v4-topic-v1-live-none

> topic feature dev set: topic rows need the pipeline to expose matched subjects

| Run | |
|---|---|
| Date | 2026-10-25 02:35 MT (-06:00) · 2026-10-25 04:35 ET (-04:00) |
| Code fingerprint (src/) | `sha256:0a0e4bd55f506bdf (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:c584f6687a180097 (2 files)` |
| Eval tool fingerprint | `sha256:1caf4d1d3f242957 (31 files)` |
| Dataset | topic requests and controls v1 (`eval/datasets/topic.v1.jsonl`, sha256:c609ca149dccc629) |
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
| Counts | 33 cases · 33 run · 0 skipped · 0 rejected by validation · 0 errors · 0 in-sample |
| Concurrency / limit | 2 / 5 |
| Wall time | 26.8 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set topic --catalog live --provider none --quiet --label v4-topic-v1-live-none` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **11/11 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 26/33 = .788 | answerable rows, first shown candidate acceptable |
| Top-5 | 25/33 = .758 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 26/33 = .788 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 11/33 = .333 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 0 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| topic | 21 | 14/21 = .667 | 13/21 = .619 | 14/21 = .667 | 0 @ n/a | 0 |
| topic_control | 12 | 12/12 = 1.000 | 12/12 = 1.000 | 12/12 = 1.000 | 11 @ 1.000 | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 0, template 145 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 29

## Degraded

- Degraded rate: 33/33 = 1.000
  - of which 33 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/33 = .000
- ai_not_configured: 33
- explanation_fallback: 29
- Interpretation source: heuristic 33

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 33 | 955 ms | 4752 ms | 5144 ms |
| Pipeline elapsed_ms | 33 | 955 ms | 4752 ms | 5144 ms |
| stage `clean` | 33 | 0 ms | 0 ms | 5 ms |
| stage `explain` | 29 | 0 ms | 6 ms | 11 ms |
| stage `interpret` | 33 | 0 ms | 0 ms | 8 ms |
| stage `judge` | 29 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 33 | 9 ms | 41 ms | 76 ms |
| stage `retrieve` | 33 | 955 ms | 4752 ms | 5144 ms |
| Open Library call (not cache hits) | 64 | 709 ms | 2827 ms | 4539 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 0 | n/a | n/a | n/a |

## Open Library calls

- Logical calls per query: mean 1.94, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 0/64 = .000
- HTTP attempts: 64 total, mean 1.94 per query; slow-empty re-queries 0
- Outcomes: ok 64
- Steps: broad 33, topic 15, any_of 6, person_fallback 5, person_fallback_any 5

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 29 | 0 | not_configured 29 | n/a | n/a |
| interpret | 33 | 0 | not_configured 33 | n/a | n/a |

- Paid LLM calls (not cache hits, incl. judge): 0
- Estimated cost: n/a (no priced calls)

## Misses (7)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| topic-06 | un livre sur les ours | ambiguous · topic: ours, bear | none (weak_evidence) |  |
| topic-07 | books like harry potter | ambiguous · topic: magic, wizard, witch, fantasy | ambiguous (weak_evidence) | OL82563W Harry Potter and the Philosopher's Stone [E]; OL82537W Harry Potter and the Chamber of Secrets [E]; OL82536W Harry Potter and the Prisoner of Azkaban [E] |
| topic-09 | a story about a girl and her horse | ambiguous · topic: horse, pony, ponies | none (no_candidates) |  |
| topic-12 | a mystery set in venice | ambiguous · topic: venice, venezia | none (weak_evidence) |  |
| topic-17 | teach me about volcanoes | ambiguous · topic: volcan* | none (weak_evidence) |  |
| topic-18 | a cookbook for italian food | ambiguous · topic: cooking, cookery | ambiguous (near_title_author) | OL25072868W Quick from scratch [C]; OL29312082W Quick from Scratch Italian Cookbook [C]; OL19984365W Good housekeeping family Italian cookbo… [C] |
| topic-20 | a biography of abraham lincoln | ambiguous · topic: lincoln | ambiguous (weak_evidence) | OL1164736W Abraham Lincoln [E]; OL2710351W Abraham Lincoln [E]; OL547941W Abraham Lincoln [E] |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
