# Eval: how every quality number is produced

`tools/FindThatBook.Eval` runs a dataset through exactly what `GET /api/books/search` runs: the API's configuration
sources (`src/FindThatBook.Api/appsettings*.json`, repo-root `.env`, environment variables), the API's composition root
(`services.AddFindThatBook(configuration)`, scopes and options validated as in the API), `QueryValidator` first (a
rejection is the API's 400), then `SearchService.SearchAsync` in its own DI scope with the endpoint's deadline. The only
things the eval adds are its instruments: the catalog mode and record-on-miss cache, the recording handler, the cache
bypass and the network guard, plus, for comparison arms only, replayed hypotheses, template-only explanations and ranker
switches. It never runs in CI; the golden replay test does (offline, below). `eval/RESULTS.md` indexes every run.

**Runs from this repo alone.** Golden, hard, topic, synthetic and held-out are all in the repo, and so are the replay
fixtures, so `run --set golden --catalog replay` works offline from a clean checkout (no `.env`, no network), and any
set runs live with only network access. Two inputs are **optional: the research archive, not part of this repo**: the
battle rounds (`--set battle`) with their cached Codex interpretations, and the local Open Library mirror
(`--catalog mirror`). Point at the archive with `--research <dir>` or `FTB_RESEARCH`; the default is a sibling folder
`../research`. Without it, those commands say what is missing, and the replay catalog uses only `tests/fixtures/golden`.

## Datasets

| Set (`--set`) | File | Version | Rows | Tuning status | Scoring |
|---|---|---|---|---|---|
| `golden` (default) | `eval/golden.v2.json` | 2 | 60 (57 answerable, 2 none, 1 rejected) | **in-sample**: every row was used while the research rules were designed and tuned; 30 rows are also in the hard set (`in_hard_set`) | the file's `scoring` block |
| `golden-v1` | `eval/golden.json` | 1 | 60 | in-sample; kept byte-for-byte to compare with the prototype's numbers | v1 block (none-04 = 200 `none`) |
| `hard` | `eval/datasets/hard.v1.jsonl` | 1 | 46 | **in-sample** (the research phase's hard set, copied unchanged) | lenient (the research prototype's eval.py), status not scored |
| `synthetic` | `eval/datasets/synthetic-mirror.v1.jsonl` | 1 | 2,000 (seed 2026) | never tuned on; machine noise over well-known works | lenient, status not scored; built for bulk mirror runs (a `--limit` slice also runs live) |
| `battle` / `battle:r1,r2` | `<research archive>/11-battle-test/rounds/r{1,2,3}/resolved.jsonl` (optional: the research archive, not part of this repo) | 1 | 1,356 (r1 416, r2 449, r3 491) | **in-sample**: the v3 fixes and the ranker's R3-xxxx rules were designed on these rounds | judge.py: book rows accept the target + split records (a folded duplicate counts), author rows any work by an accepted author key; a match on another record of the intended book is a `dup_record_match`, not severe. Label: **"synthetic adversarial, local mirror (no editions.title/alt names/person:), relative comparisons only"** |
| `eval/datasets/battle-live-sample.v1.jsonl` | (path) | 1 | 200 battle rows (seed 2026): 14 each from author_alias, pen_name, pen_name_obscure, jp_en_duplicates, plot, translation, decorated_titles, series, plus a proportional slice of the other 25 categories | **in-sample**: used to decide which ranker rules stay (live ablation) | judge.py, as `battle` |
| `topic` (`topic-v1`) | `eval/datasets/topic.v2.jsonl` (`topic.v1.jsonl` kept) | 2 | 38: 24 topic requests, 13 controls, 1 topic + author with no hits (v1: 33) | dev set for the topic-search feature: written before the feature and used while building it, so in-sample | topic rows: status `ambiguous` with reason `topic_search` and ≥ 3 of the top 5 with a subject matching a topic word (word + optional s/es, `word*` prefix, phrases as phrases); controls are real titles containing request/topic words ("about a boy", "find me aciman"), scored like golden rows with ids resolved live on 2026-10-25. Subjects come from the work's own `CandidateWork.Subjects` (filled for works from topic calls), falling back to `Evidence.MatchedSubjects`; results rows also keep the request's `topic` decision and `suggested_by_interpreter` per shown work |
| `heldout` | `eval/heldout.json` | 1 | 409 | **SEALED**: never tune on it. The runner refuses it without `--heldout` and labels every output HELD-OUT | its own block (a wrong `match` is also a status error) |

Golden v2 differs from v1 in one expectation: **none-04** (`?!? ... ###`) now expects a 400 `validation_failed`
(requirement R2 in `docs/TRACEABILITY.md`, decision DE-05 in `docs/FINDINGS-STATUS.md`: the query has no letter or
digit in any script). v2 also adds the per-row
`in_sample` / `in_hard_set` flags and a `changelog`. `DatasetTests` proves nothing else changed. Any `--set <path>`
also works: golden-format `.json` or gen_queries-format `.jsonl` (an optional first line `{"_meta": {...}}` carries
name, version, tuning and label).

The synthetic set was generated once with `python3 -I ol-mirror/gen_queries.py 2000 2026` (dev-only Python, not a
dependency) from the mirror DB built from the 2026-09-30 Open Library dumps; its `_meta` line records the command.

## Catalog modes (`--catalog`)

| Mode | What answers search.json | Use it for | Bias to state next to any number |
|---|---|---|---|
| `live` (default) | openlibrary.org through the real adapter (UA, ≤ 3 req/s throttle, retries, cache) | headline numbers, latency | Open Library changes over time; results are dated |
| `replay` | recorded responses, matched on q/title/author/author_key/sort (fields and limit ignored; a recording that covers the limit, then one with `person`, then one with v3 fields wins) | offline regression, prototype comparison | No HTTP: Open Library latency and attempts are n/a. `tests/fixtures/golden` was re-recorded live on 2026-10-25 with v3's `editions.title` / `author_alternative_name` fields; the research archive's older caches predate them, so edition-title and alias evidence cannot fire on those (`_manifest.json` flags each fixture's `has_v3_fields`). A miss returns an empty result **and** is reported as `replay_miss` per row; the summary also prints the headline without those rows |
| `mirror` | the local mirror at `http://127.0.0.1:8090/` (`--mirror-url`) through the real adapter, behind the record-on-miss cache `eval/results/_catalog-cache/raw/mirror/` (gitignored; `--catalog-cache off` to bypass) | bulk synthetic and battle runs, ablations | **"synthetic, local mirror (bm25, OR fallback), not Open Library's ranking"**: FTS5 bm25 + edition count instead of Solr; silently falls back to OR when AND finds nothing; ignores `author_key` and `sort`; no `editions.title` / `author_alternative_name`, so rules that read them (alias authors, edition titles, the interpreter-author check's alternative names) are handicapped there. It has no person index, so `person:` calls are answered empty without a call (counted as `unsupported`). Mirror mode raises the adapter's timeouts by configuration (AttemptTimeout 40 s, TotalTimeout 90 s, SlowEmptyThreshold 35 s, Search:RequestDeadline 2 min) because the OR fallback can take 10–30 s |

The mirror (`ol-mirror/server.py`, built from the Open Library dumps) is part of the optional research archive, not
of this repo; start it with `python3 server.py 8090` before a mirror run.
Default replay sources: `tests/fixtures/golden`, then, only when the research archive is present, its
`{12-pipeline-v3/live_cache, 11-battle-test/live_cache, 07-fix-misses/cache, 05-eval-proto/cache}` (read-only);
`--replay-from <dir>` (repeatable) replaces them.

**Record-on-miss cache** (`--catalog-cache <dir>`; default on for the mirror, opt-in for live): a request found in the
folder is answered from disk; anything else goes to the real adapter, the recording handler saves the response there,
and the answer is mapped from that recording, so a fetched and a replayed response go through the same code. Arms of
an ablation therefore see identical catalog data. Keep one folder per backend (never mix mirror and live).

## Comparison arms (one LLM run, many measurements)

- `--hypotheses <file>`: replays interpretations instead of calling an LLM, keyed by the validated query. The file is
  an eval `results.jsonl` (every run stores `meta.hypothesis` and the interpret call, failures included) or a research
  cache line `{"query","hyp"}` (in the optional research archive: `11-battle-test/cache/hyp_v3.jsonl` covers all
  1,356 battle queries, `12-pipeline-v3/cache/hyp_v3.jsonl` 865 of them; both produced by OpenAI Codex with prompt v3). Every answer still goes through the
  pipeline's `InterpretationService` sanitizer. A query with no entry is reported as `hypothesis_miss` and runs LLM-off,
  or, with `--hypotheses-fill live`, is interpreted live (use it only with a file recorded on the same build and prompt).
- `--explain template`: never calls the explain LLM (ranking metrics are unaffected; the summary separates the
  `explanation_fallback` this causes). Measure explanation quality and grounding on golden with `--explain llm`.
- `--ranker default|baseline` and `--ranker-off Switch,Switch`: the ranker's rule switches (`Core/Matching/RankerOptions`,
  discovered automatically, so a new rule is ablated without an eval change). `baseline` (alias `v3`) = the measured
  prototype rules, every addition off.
- `ablate`: the default arm once, then one arm per switch turned off, then the baseline arm, all on the same catalog
  responses and the same hypotheses (it refuses to call an LLM). `--switches A,B` limits the arms; `--max-ol-requests`
  is a budget for the whole ablation; `ablate --render <folder>` rebuilds the table from the arms' results. `ablation.md` ranks the switches by net rows
  (rows the rule wins minus rows it loses) with deltas for top-1, top-5, status, match precision and severe matches.

## Commands

```bash
E="dotnet run --project tools/FindThatBook.Eval --"

$E run --set golden --catalog replay                        # offline, LLM off: the regression arm (seconds)
$E run --set golden --catalog live                          # live Open Library, LLM off
$E run --set golden --catalog live --provider gemini --max-llm-calls 150            # LLM on (≈ 2 calls per query)
$E run --set golden --catalog live --provider gemini --judge on --max-llm-calls 200 # + Decisions judge
$E compare eval/results/<date>-golden-v2-live-none eval/results/<date>-golden-v2-live-gemini   # LLM on vs off
$E stability --mode interpret --provider gemini --runs 3 --limit 20 --max-llm-calls 60   # parse stability
$E stability --mode retrieval --runs 3 --limit 20                                       # Open Library variability, LLM off
$E run --set synthetic --catalog mirror --limit 500                                     # bulk synthetic
$E run --set heldout --heldout --catalog live --provider gemini --max-llm-calls 900     # FINAL only
$E fixtures        # offline: rebuild tests/fixtures/golden from what the replay run used
$E record          # live, LLM off: re-record tests/fixtures/golden (merge; --replace for a clean set)
H=../research/11-battle-test/cache/hyp_v3.jsonl   # optional: the research archive, not part of this repo
$E run --set battle --catalog mirror --hypotheses $H --ranker baseline                  # $0: Codex v3 interpretations
$E ablate --set battle --catalog mirror --hypotheses $H                                 # $0: per-rule ablation
$E ablate --set battle --catalog mirror                                                 # $0: same, LLM off
$E run --set golden --catalog live --provider gemini --judge on --hypotheses eval/results/<llm-on run>/results.jsonl
                                                                                        # judge arm, no new interpret calls
$E index --builds 918d7b88 --binaries bb794a4f   # rebuild eval/RESULTS.md; runs on these src / binary prints are "final"
```

Other options: `--model <id>`, `--limit N` (first N rows, for cheap runs), `--concurrency N` (live default 2),
`--out <dir>` / `--label <text>` (default `eval/results/<date>-<set>-<catalog>-<provider>[-judge][-HELDOUT]`, never
overwritten), `--no-cache` (pass-through HybridCache + `AI:Cache:Enabled=false`), `--max-ol-requests N`,
`--llm-rpm N`, `--env Development`, `--config Key=Value` (any API setting), `--verbose`, `--quiet`,
`--catalog-cache <dir>|off`, `--hypotheses <file>`, `--explain llm|template`, `--ranker default|baseline`, `--ranker-off A,B`,
`--research <dir>` (the optional research archive), `--resume <run folder>`, `--max-provider-error-rate R`.

Configuration the eval sets per mode (everything else is the API's): `AI:Provider`, `AI:{Gemini|Anthropic|OpenAI}:Model`
(`--model`), `Judge:Enabled`, mirror: `OpenLibrary:BaseUrl`, `OpenLibrary:RequestsPerSecond=10` and the longer
timeouts above, `--no-cache`: `AI:Cache:Enabled=false`. `provider none` is the LLM-off arm (heuristic interpretation, template explanations; the
API reports it as degraded `ai_not_configured`, and so does the eval).

## Outputs (one folder per run)

- `results.jsonl`: per query: id, query, category, in_sample, held_out, expected, status (`match|ambiguous|none|rejected|error|skipped`),
  reason, error_code, the shown candidates (work id, title, authors, tier, score, second_opinion, explanation source),
  pass flags (top1, top5, status, match_correct, false_match, target_rank) and meta (wall and pipeline ms, stage
  timings, hypothesis, degraded reasons, every catalog call with cache hit/attempts, every LLM call with tokens, judge
  verdict, explanation sources, estimated cost, replay hits/misses).
- `summary.md`: stamped with the date (MT and ET), a git-free code fingerprint (SHA-256 of `src/` + build files, read at
  process start), the SHA-256 of the pipeline binaries actually loaded (Core + Infrastructure) and the eval tool's own
  fingerprint, dataset name/version/hash, provider/model requested and observed, judge, catalog mode,
  counts. Sections: headline, per category, replay, judge, explanations, degraded, latency, Open Library calls, LLM
  usage and cost, misses with the top 3, and (golden/hard only) the prototype's numbers in a block labelled **not this build**.
- `summary.json`: the same numbers for tools; `compare` reads `results.jsonl` + `summary.json`.
- `raw/outcomes.jsonl`: full pipeline outcomes (ranking top 10, explanations), for runs of ≤ 200 rows. Gitignored.
- Runs of more than 300 rows write `results.jsonl` to `raw/` (gitignored, ~3 KB a row); `compare`, `--hypotheses`
  and `index` find it there. Ablation arms other than the default live under `raw/arms/`.

| Metric | Definition |
|---|---|
| **match precision** (headline) | rows returned as `match` whose top-1 is right and whose row allows `match` |
| top-1 / top-5 | answerable rows; first shown / any of the first five shown candidates acceptable |
| status accuracy | outcome ∈ the row's statuses; `rejected` must carry the expected error code |
| coverage | answerable rows returned as `match` |
| false matches | `match` on a row with no answer (none / rejected rows) |
| severe / duplicate-record matches | severe = wrong or false `match` (battle judge.py); duplicate-record = `match` on another record of the intended book (reported, not severe) |
| provider-error rows | rows with a provider timeout / 5xx / 429 on an LLM call, reported apart from model quality and from our own budget skips |
| topic rows | status `ambiguous` + reason `topic_search`; top-5 = ≥ 3 of the first five on topic by subject |
| judge agreement | agreed / (agreed + disagreed + chose_none); judge pick vs code #1 correctness on the same rows |
| explanations | llm vs template among shown candidates; grounding rejections = template shown although the explain call succeeded |
| degraded | share of searched rows with any `degraded_reasons`, by reason |
| latency | p50/p95/max: wall, pipeline `elapsed_ms`, each stage, Open Library calls, LLM calls by operation |
| Open Library | logical calls per query, cache hits, HTTP attempts, outcomes, steps |
| LLM cost | paid calls (not cache hits), tokens, IAiPricing estimate per 1,000 searches; unpriced calls make it a lower bound |
| parse stability | `stability --mode interpret`: share of queries whose normalized title+author (and each field) is identical in all N runs, LLM cache bypassed |
| retrieval variability | `stability --mode retrieval`: top-1 / top-5 set / status identical across N live runs, mean pairwise Jaccard of the ranked pool, caches bypassed |

## Golden replay regression test

`tests/FindThatBook.Api.Tests/Golden/GoldenReplayTests.cs` runs golden v2 with the LLM off, the judge off, only
`tests/fixtures/golden`, `.env` ignored and every HTTP request blocked. It fails on any replay miss, any error, a
`match` on a no-answer row, none-04 not rejected, a non-deterministic second run, or a drop below the floors set from
the run on fixtures re-recorded live on 2026-10-25 (all with v3 fields): **top-1 ≥ 56/57, top-5 ≥ 56/57, status ≥
56/60, match precision 41/41**. Raise floors when the pipeline improves; never lower one without a line here saying why.
Lowered once (2026-10-25): top-5 and status 57 → 56. character-04 holden caulfield (LLM off): the
re-recorded live data turns a former wrong match into an honest none (the only evidence for The Catcher in the Rye is
an Italian edition title); with the LLM on, the interpreter supplies the title. Precision rose 39/40 → 41/41. When the pipeline's requests
change, rebuild the fixtures (`fixtures` offline; `record` for requests no recording covers) and review the diff.
`tests/fixtures/golden/_manifest.json` lists every file's request, origin and the rows that use it.

## Budgets and cost

- **Gemini is light.** `--max-llm-calls` defaults to 20 paid calls (interpret + explain + judge, cache hits free); the
  runner reserves each query's worst case before it starts, so it never overshoots, and later rows are reported
  `skipped`. LLM calls are paced to the active provider's `AI:<Provider>:RequestsPerMinute` (Gemini free tier: 15 per
  minute per model, measured): 90% of it, and with `--explain llm` at most rpm − `AI:Budget:ExplainReserve` − 1
  (Gemini: 11/min), because the API keeps the last permits of each minute for interpret calls. The API's own budget
  guard therefore never turns the LLM-on arm into a heuristic one (a golden LLM-on run of ≈ 120 calls takes ≈ 11 min).
  Explain calls the reserve still refuses are counted (`explain_reserve`), not treated as errors. Check
  `budget_exhausted` / `rate_limited` in the LLM table before quoting a number.
- **Reuse one LLM run**: judge on/off, ranker arms and re-scoring take `--hypotheses <results.jsonl>` instead of calling
  the interpreter again; bulk and held-out runs can take `--explain template`. Summaries of such runs say which build
  recorded the interpretations and which build re-ran ranking.
- **Provider outages are not model quality.** With an LLM on, a run stops when more than 20% of searched rows hit a
  provider error (timeout, 5xx, 429; `--max-provider-error-rate`); `--resume <run folder>` later re-runs only the skipped
  and spoiled rows and keeps the rest. Summaries count provider-error rows separately from our own budget skips.
- **Live Open Library ≤ 3 req/s**: enforced by the adapter's throttle (the eval never bypasses it); keep `--concurrency`
  at 2 for live runs. LLM-off golden ≈ 90 logical calls.
- Measured (golden v2 live, 59 searches, 2026-10-25, list prices via IAiPricing): Gemini 3.5 flash-lite interpret +
  explain ≈ $0.96 per 1,000 searches (113 paid calls, most of it the explain call); interpret only: Claude Haiku 5.5
  ≈ $0.11, GPT-4.1-mini ≈ $0.22; Decisions judge ≈ $0.07. See `eval/RESULTS.md`.

## Honesty rules

1. Every number quoted anywhere comes from a `summary.md` produced by this tool, with its date and fingerprint.
2. Only measured values; anything not measured prints `n/a`, never 0 or an estimate.
3. Golden and hard are in-sample. Generalization claims use the held-out set, run once at the end with `--heldout`.
   Never change a rule, threshold, prompt or few-shot because of a held-out row.
4. Mirror numbers always carry the synthetic label; battle numbers carry theirs ("relative comparisons only");
   replay numbers carry the replay bias; prototype numbers are always labelled as the Python prototype's.
5. Report misses and regressions next to wins (the summary's misses table, `compare`'s flips). One golden query is
   ≈ 1.8 points of top-1.
