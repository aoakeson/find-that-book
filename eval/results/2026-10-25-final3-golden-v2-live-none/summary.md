# Eval: Find That Book golden set v2 · final3-golden-v2-live-none

| Run | |
|---|---|
| Date | 2026-10-25 03:29 MT (-06:00) · 2026-10-25 05:29 ET (-04:00) |
| Code fingerprint (src/) | `sha256:918d7b88cc5867cf (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:bb794a4ff02178f3 (2 files)` |
| Eval tool fingerprint | `sha256:bfa0664db82124b7 (31 files)` |
| Dataset | Find That Book golden set v2 (`eval/golden.v2.json`, sha256:5b70cc75033b87c6) |
| Held out | no |
| Tuning | in_sample: Every row was run while the research rules were designed and tuned (research/05-08, the plan 04 fixes, the plan 06 v3 ablations replayed this set on cached live responses). in_hard_set marks the 30 queries that are also in the research hard set (research/07-fix-misses/hard.jsonl, 46 rows), which was in-sample too. Numbers on this set measure regressions and fidelity to the prototype, not generalization. |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | none |
| Models (observed in traces) | none (no LLM call reached a provider) |
| Interpretations | none (LLM off) |
| Explanations | template (LLM off) |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 60 cases · 60 run · 0 skipped · 1 rejected by validation · 0 errors · 60 in-sample |
| Concurrency / limit | 2 / 5 |
| Wall time | 36.1 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set golden --catalog live --provider none --quiet --label final3-golden-v2-live-none` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **41/41 = 1.000** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 56/57 = .982 | answerable rows, first shown candidate acceptable |
| Top-5 | 56/57 = .982 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 56/60 = .933 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 41/57 = .719 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 3 | any `match` where no book is right |
| Severe matches | 0 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| ambiguous | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 4/4 = 1.000 | 2 @ 1.000 | 0 |
| author_only | 5 | 5/5 = 1.000 | 5/5 = 1.000 | 5/5 = 1.000 | 0 @ n/a | 0 |
| brief | 6 | 6/6 = 1.000 | 6/6 = 1.000 | 4/6 = .667 | 3 @ 1.000 | 0 |
| character | 5 | 4/5 = .800 | 4/5 = .800 | 3/5 = .600 | 0 @ n/a | 0 |
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

- Source mix (shown candidates): llm 0, template 277 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 56

## Degraded

- Degraded rate: 59/59 = 1.000
  - of which 59 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/59 = .000
- ai_not_configured: 59
- explanation_fallback: 56
- Interpretation source: heuristic 59

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 59 | 780 ms | 4919 ms | 5709 ms |
| Pipeline elapsed_ms | 59 | 780 ms | 4919 ms | 5709 ms |
| stage `clean` | 59 | 0 ms | 0 ms | 4 ms |
| stage `explain` | 56 | 0 ms | 5 ms | 9 ms |
| stage `interpret` | 59 | 0 ms | 0 ms | 7 ms |
| stage `judge` | 56 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 59 | 9 ms | 36 ms | 69 ms |
| stage `retrieve` | 59 | 779 ms | 4919 ms | 5705 ms |
| Open Library call (not cache hits) | 85 | 573 ms | 1994 ms | 4835 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 0 | n/a | n/a | n/a |

## Open Library calls

- Logical calls per query: mean 1.53, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 5/90 = .056
- HTTP attempts: 85 total, mean 1.44 per query; slow-empty re-queries 0
- Outcomes: ok 90
- Steps: broad 59, any_of 14, person_fallback 8, author_works 6, person_fallback_any 3

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 56 | 0 | not_configured 56 | n/a | n/a |
| interpret | 59 | 0 | not_configured 59 | n/a | n/a |

- Paid LLM calls (not cache hits, incl. judge): 0
- Estimated cost: n/a (no priced calls)

## Misses (4)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| brief-01 | mark huckleberry | match · Adventures of Huckleberry Finn | ambiguous (near_title_author), target at #1 | OL53908W Adventures of Huckleberry Finn [C]; OL53919W The Adventures of Tom Sawyer [D]; OL413674W Tom Sawyer Abroad [D] |
| brief-06 | austen bennet | match · Pride and Prejudice | ambiguous (author_only), target at #1 | OL66554W Pride and Prejudice [D]; OL66513W Emma [D]; OL66562W Sense and Sensibility [D] |
| character-02 | ahab whale | match · Moby Dick | ambiguous (weak_evidence), target at #1 | OL102749W Moby Dick [E]; OL1938304W The Snail and the Whale [E]; OL3900237W Whale (DK Eyewitness Books) [E] |
| character-04 | holden caulfield | match/ambiguous · The Catcher in the Rye | none (weak_evidence) |  |

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
