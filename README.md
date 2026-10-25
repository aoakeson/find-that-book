# Find That Book

Type a messy line like `tolkien hobbit illustrated deluxe 1937`, `mark huckleberry` or `austen bennet`; get back the
Open Library work it most likely means, ranked candidates when it's unclear, and a one-sentence "why this book" for each.

**AI interprets, code decides, every step is measured.** An LLM reads the text into a search hypothesis (title, author,
characters, English and original-language titles) and writes the explanation sentences from validated facts.
Deterministic, tested C# decides which Open Library records come back, in what order, and whether we can honestly say
`match`. If the LLM is down, slow or out of quota, the search still works (`degraded`), never an error.

## Quick start

1. Install [Node.js 24](https://nodejs.org).
2. From the repo root:

```
npm run dev
```

3. Open http://localhost:5173

The first run installs the web packages and the .NET 10 SDK if they are missing. Same command on macOS and Windows.
Ctrl+C stops the app.

An AI key is optional. Paste a [free Gemini key](https://aistudio.google.com/apikey) at http://localhost:5173/admin, or
copy `.env.example` to `.env` and set `GEMINI_API_KEY`.

Ports, Docker, and troubleshooting: [docs/RUNNING.md](docs/RUNNING.md).

## Contents
- [Quick start](#quick-start) · [AI setup and model choice](#ai-setup-and-model-choice) · [Configuration](#configuration-and-keys)
- [How it works](#how-it-works) · [Topic requests](#natural-sentence-and-topic-requests) · [The matching hierarchy](#the-matching-hierarchy) · [Failures and unclear results](#failures-and-unclear-results)
- [API](#api) · [Features vs the brief](#features-vs-the-brief) · [Assumptions and trade-offs](#assumptions-and-trade-offs)
- [Testing](#testing) · [Evaluation results](#evaluation-results) · [Known misses](#known-misses)
- [How I used AI tools](#how-i-used-ai-tools) · [What I'd do with more time](#what-id-do-with-more-time) · [Docs map](#docs-map) · [Frontend](#frontend)

## API only

`npm run dev` starts the web app and the API. To run the API by itself:

```
dotnet test
dotnet run --project src/FindThatBook.Api
```

Open http://localhost:5080/scalar, or:

```
curl "http://localhost:5080/api/books/search?q=tolkien%20hobbit%20illustrated%20deluxe%201937"
```

- **No key?** It still runs: the heuristic arm searches with the cleaned words, explanations come from templates, and
  responses say `degraded_reasons: ["ai_not_configured"]`.
- **Free Gemini tier:** 15 requests per minute and 500 requests per day per model (both measured from Gemini's 429
  quota details). A search uses up to 2 (interpret + explain), so more than ~6 searches a minute, or ~250 a day,
  degrade: explanations fall back to templates first, then interpretation. Repeated searches are served from the 24 h
  LLM cache. With an Anthropic or OpenAI key set as well you can switch provider on the Admin page.
- Nothing here needs Python, Docker or the local Open Library mirror; those are optional tools for bulk evaluation.

## AI setup and model choice

| Provider | Default model | Key (any of) | Status |
|---|---|---|---|
| **Gemini (default)** | `gemini-3.5-flash-lite` | `GEMINI_API_KEY`, `GOOGLE_API_KEY`, `AI__Gemini__ApiKey` | supported: smoke-tested through our adapter |
| Anthropic | `claude-haiku-5-5` | `ANTHROPIC_API_KEY`, `AI__Anthropic__ApiKey` | supported: smoke-tested |
| OpenAI | `gpt-4.1-mini` | `OPENAI_API_KEY`, `AI__OpenAI__ApiKey` | supported: smoke-tested |
| none | — | — | heuristic + templates |

By default (`AI__Provider=auto`) the app uses the first provider that has a key, in the order Gemini → Anthropic →
OpenAI, so a key alone is enough. To pin one, set `AI__Provider=anthropic` (or `gemini`, `openai`, `none`) in `.env`, an
environment variable, or the Admin page / admin API. A provider counts as supported only after a real structured call through our adapter passed
([docs/ai-smoke.md](docs/ai-smoke.md)). Why these models: the interpreter sits on the request path, so we want the
strongest model with sub-second-ish latency and no reasoning budget. `gemini-3.5-flash-lite` answered in ~0.8 s p50 in the
smoke test (thinking minimal); `gemini-3.8-flash` was tried and rejected (400 on minimal thinking, timeouts at low);
`gpt-5-nano` was rejected after it spent its whole token budget on reasoning and returned empty JSON.

**Optional second opinion (OpenAI Decisions, off by default):** `Judge__Enabled=true` plus `OPENAI_API_KEY`. It only
gates status (`match` vs `ambiguous`) and never reorders results ([ADR 0004](docs/adr/0004-decisions-judge-status-gate.md)).

## Configuration and keys

The simplest ways to add a key are in the [Quick start](#quick-start): paste it on the Admin page of the app (lasts
until restart) or put it in `.env` (lasts). Precedence, lowest to highest: `appsettings.json` → `appsettings.{Environment}.json` → user secrets (Development) →
repo-root `.env` → environment variables (well-known names like `GEMINI_API_KEY`, then `AI__Gemini__ApiKey` style) →
command line → runtime admin changes. `.env.example` lists every setting.

- Keys never appear in logs, errors, OpenAPI, fixtures or responses; the admin API shows only whether a key is set, where
  it came from (`environment`, `dotenv`, `user_secrets`, `appsettings`, `runtime`) and a short SHA-256 fingerprint.
- **Admin API (Development only, loopback callers only; 404 otherwise):** `GET /api/admin/ai`, `PUT /api/admin/ai`
  (provider, models, judge on/off), `PUT|DELETE /api/admin/ai/keys/{provider}`, `POST /api/admin/ai/probe/{provider}`.
  Changes live in memory and are lost on restart; `.env` is never rewritten. In production use environment variables.
- Cost controls: per-provider requests/minute (Gemini 15), interpretation has priority over explanations, a pause after
  any provider 429, a daily estimated-cost cap ($0.50 LLM, $0.50 judge), per-IP and global rate limits on search.

## How it works

```
GET /api/books/search?q=…
  │ validate (1–300 chars, a letter or digit in any script, not only stop words)        → 400 ProblemDetails
  ▼
clean: fold accents (all scripts kept), & → and, drop format words, keep a year as a soft bonus
  ├─ interpret (LLM → title/author/characters/original title; cached 24 h)   ┐ in parallel: the LLM
  └─ call 1: search.json?q=… (title= when shorter than 3 characters)         ┘ hides behind Open Library
  ▼
hypothesis calls: title=&author= from the LLM's words; the original-language title
fallback ladder, one call at a time while the evidence is weak: person:(…) for characters → OR-join for typos
  → character fallback → an author's most-published works (author-only queries)   ≤ 5 logical calls, 25 s deadline
  ▼
resolve primary authors vs contributors · fold duplicate records · tier A–E · score · order   (pure C#, deterministic)
  ├─ explain: one batched LLM call over facts only → grounding check → else template      ┐ in parallel
  └─ judge (optional): Decisions picks among the deduped candidates → may only gate status ┘
  ▼
200 { status: match | ambiguous | none, hint, degraded, interpretation, candidates[], meta{ timings, calls, tokens, cost } }
```

```
src/FindThatBook.Core            pure C#, no package references: normalization, matching, status, ports, orchestration
src/FindThatBook.Infrastructure  Open Library client (resilience, throttle, cache), AI adapters, judge, suggest index, DI root
src/FindThatBook.Api             minimal API, validation, ProblemDetails, OpenAPI + Scalar, rate limits, admin
tests/…Core.Tests · …Infrastructure.Tests · …Api.Tests                       tools/FindThatBook.Eval (eval harness)
```

**Why only `search.json` on the hot path.** The brief lists `/works/{id}`, `/authors/{id}` and `/authors/{id}/works.json`.
We evaluated them: a `search.json` document already carries `author_key` in the same order as the work's `works.authors`,
plus `contributor` roles, the best-matching `editions.title` and `author_alternative_name`, so primary authors are
resolved from data in hand (the most-edited same-title work's authors are primary; role-labelled names are
contributors). Work records carry author roles on ~1% of works, `/authors/{id}/works.json` is ordered by last
modification (useless for "top works"; we use `search.json?author_key=…&sort=editions`), and every extra Open Library
call costs ~3 s at p50. So each query makes 1–3 logical calls typically, at most 5.

## Natural-sentence and topic requests

"find me a book about bears", "books like harry potter", "a novel set in paris", "un livre sur les ours" ask for
*books about something*, not one book. The interpreter (prompt `interpret.v5`) labels the request `book` or `topic`
and, for a topic, gives 1–3 English subject headings and up to 5 well-known books that fit. Code still decides:

- Retrieval: the normal broad search runs as always (so real titles like *About a Boy*, *Much Ado About Nothing*,
  *Looking for Alaska* or Aciman's *Find Me* still win), plus one subject search on Open Library (`q=subject:(bears)`,
  relevance order) and one call that checks the suggested titles exist.
- Evidence: a book counts as on-topic only if its own Open Library subject headings contain the topic word (whole
  word, singular or plural); a suggestion is shown only if Open Library has it, and its explanation never claims a
  topic it can't show ("Suggested for this topic; …").
- Status: always `ambiguous` with `status_reason: "topic_search"` (several fitting books, never one claimed `match`),
  `interpretation.intent: "topic"`, `interpretation.topics`, and `matched_subjects` on each card. The judge never runs.
- Without the LLM, a conservative cleaner rule handles the common English frames ("a book / books / novels / something
  about | on | set in X", leading "find me", "can you recommend", "I'm looking for a book called …"); the interpreter
  covers other languages, "books like …" and genre wording. A request with no usable topic ("recommend me a good book")
  is `none` with the hint "Add a topic, a title or an author."

## The matching hierarchy

| Tier | Rule (brief §4) | Example |
|---|---|---|
| **A** | exact/normalized title + primary author (or an exact title with no author named) | `tolkien hobbit` → *The Hobbit* |
| **B** | exact title + contributor-only author | `hobbit dixon` → the graphic novel; Tolkien primary, Dixon adapter |
| **C** | near title + author | `mark huckleberry` (LLM off) |
| **D** | author only: the author's most-published works | `dickens` |
| **E** | no clear winner: partial title, keywords or characters | `scout atticus` (LLM off) |

**The tiers decide what we claim; the score usually decides the order.** A typed exact title by its primary author with at least 10 editions moves ahead of near-title and author-only results; tiny exact-title records keep the score order. Only a tier A/B top can be `match`. Ordering strictly
A→E measured worse because Open Library has junk records with exact titles (a 2-edition "Harry Potter" box set beats
*Philosopher's Stone* on title alone), so candidates are ordered by a score:
`4·title similarity + 2·author role + 2.5·log10(editions) + 0.5·log10(reading logs) + 0.5·year ±1 + 4·character coverage
+ 2 (tier B) − penalties` (study guides, omnibus sets, adaptations unless asked for, an author the query didn't name),
with ties broken by edition count, then work id. A handful of guarded rules on top fix specific failure classes
(series names, tiny duplicate records, typed subtitles/volumes, a title mashed from two books); each is switchable and
measured in the eval ([results](#evaluation-results)).

Normalization details: accents folded without dropping letters of any script (三体, Анна Каренина, 채식주의자 stay
intact), `&` → and, number words ↔ digits for comparison (`1984` = *Nineteen Eighty-Four*), subtitles split on `: ; —`,
typo-tolerant token matching where an exact token always beats a fuzzy one (`the little prince` is not *A Little
Princess*).

## Failures and unclear results

| Situation | Response |
|---|---|
| Clear winner | 200 `match` |
| Several plausible works (author-only, close scores, single word, characters only) | 200 `ambiguous`, up to 5 ordered candidates + `hint` |
| Nothing usable | 200 `none`, `candidates: []`, `hint` |
| LLM missing, slow, rate-limited, malformed, over budget | 200 with `degraded: true` and the reason; heuristic parse and/or template sentences |
| A later Open Library call fails or the deadline passes | 200 with what was found, `degraded_reasons: ["catalog_partial" / "deadline_reached"]` |
| Open Library down and nothing retrieved | 503 `catalog_unavailable` (Retry-After) · 504 `catalog_timeout` / `search_timeout` · 502 `catalog_bad_response` |
| Bad input | 400 `validation_failed` / `no_searchable_terms`, `errors.Q` |
| Too many requests | 429 `rate_limited` + Retry-After (see [Rate limits](docs/RUNNING.md#rate-limits-and-concurrency)) |

Open Library calls: a 3 req/s token bucket inside the retry handler, 8 s per attempt, 15 s per call, 2 retries with
jitter only for timeouts/408/429/5xx (400/404/422 are sent once), a circuit breaker, and a slow empty answer (a 200 with
0 hits after 6 s, which Open Library sometimes returns under load) is re-asked once and never cached.

## API

`GET /api/books/search?q=&limit=` · `GET /api/suggest?prefix=&limit=` · `GET /api/health` · OpenAPI 3.1 at
`/openapi/v1.json`, Scalar UI at `/scalar`. Field-by-field contract for the frontend: [docs/API.md](docs/API.md). The
brief's fields (`title`, `author`, `first_publish_year`, `explanation`) appear byte-for-byte; `meta` reports what the
request actually did (stage timings, Open Library calls/cache hits/HTTP attempts, LLM calls/tokens/estimated cost),
never estimates presented as measurements.

## Features vs the brief

| Brief | Where |
|---|---|
| 1 Messy queries: sparse, dense/noisy, ambiguous | `QueryCleaner`, `LlmQueryInterpreter`, retrieval ladder |
| 2a AI field extraction `{title?, author?, keywords[]}` | `LlmQueryInterpreter` (prompt `interpret.v5`: also intent, subjects, suggestions), `HypothesisSanitizer` |
| 1 Natural sentences ("find me a book about bears") | `TopicRequest`, cleaner request/topic frames, subject + suggestion calls, `topic_search` status |
| 2b Grounded 1–2 sentence explanation | `ExplanationService` → `LlmExplanationWriter` (facts only) → `GroundingCheck` → `TemplateExplainer` |
| 2c Optional re-rank | the Decisions judge, as a calibrated status gate rather than a re-ranker (measured: re-ranking hurt) |
| 3 Open Library search + details, normalization, subtitles, dedupe, primary authors | `OpenLibraryCatalog`, `TitleText`, `MatchRanker` (see "Why only search.json") |
| 4a–e Matching hierarchy | `MatchRanker`, `StatusPolicy` |
| Contributors as lower signal, named in explanations | contributor-role parsing + tier B + explanation facts |
| Return title, primary authors, year, OL ids/links, cover, explanation | `SearchResponse` |
| README, AI setup, Gemini default | this file |
| Simple modern UI | React app in `web/` (see [Frontend](#frontend) and [docs/FRONTEND.md](docs/FRONTEND.md)) |
| Live demo | [books.andrewoakeson.com](https://books.andrewoakeson.com/) (Fly.io; see [deployment](docs/DEPLOYMENT.md)) |

Traceability (brief → code → tests) and the edge-case ledger: [docs/TRACEABILITY.md](docs/TRACEABILITY.md),
[docs/FINDINGS-STATUS.md](docs/FINDINGS-STATUS.md).

## Assumptions and trade-offs

- **.NET 10 instead of .NET 8:** .NET 8 leaves support on 2026-11-10; .NET 10 is the current LTS ([ADR 0002](docs/adr/0002-dotnet-10.md)).
- `first_publish_year` is shown as Open Library has it but never filtered on; it is wrong for some classics (*A Tale of
  Two Cities* = 1800). Explanations mention a year only when the user typed one and it matched.
- Short and non-Latin queries are valid (`it`, `三体`); Open Library's `q=` rejects text under 3 characters, so those are
  sent as `title=`.
- A search can take 2–5 s: Open Library's own latency dominates; the LLM interpretation runs beside the first call,
  the explanation call adds ~1–2 s after ranking.
- The judge is off by default: it needs a second vendor and trades coverage for precision.
- In-memory caches only (no Redis); the API is one process.

## Testing

| Project | What | Style |
|---|---|---|
| `FindThatBook.Core.Tests` | normalization, matching, status, ladder, explanations/grounding, orchestration with fakes | table-driven `[Theory]`s over real recorded Open Library pools |
| `FindThatBook.Infrastructure.Tests` | Open Library client on recorded JSON (WireMock), retry/no-retry table, throttle, breaker, cache; AI adapters with fake chat clients and the real SDKs over a fake HTTP handler; judge; suggest index | integration over fakes |
| `FindThatBook.Api.Tests` | contract (approved JSON), validation, ProblemDetails, rate limits, admin protection, OpenAPI examples, offline golden replay with floors | `WebApplicationFactory` |

The frontend has its own unit, contract and Playwright tests (`npm test`, see [docs/FRONTEND.md](docs/FRONTEND.md)); its
contract test checks the TypeScript types against `/openapi/v1.json` and runs the OpenAPI examples through the UI.

Run: `dotnet test` (no network, no keys; ~30 s). On the final build: 3,103 cases, 3,098 passed, 0 failed, 5 skipped.
The skipped ones are opt-in live checks. In a checkout without the optional research archive one more skips: the
battle-round loader. The cases come from about 690 test methods, and most cases are rows of a few data-driven replays
over recorded Open Library pools.

CI doesn't test live Open Library or LLM quality, since they're non-deterministic and need keys. The eval tool measures
them instead ([ADR 0005](docs/adr/0005-fixtures-and-eval-not-live-tests.md)). Its offline check needs no keys or
network:

```
dotnet run --project tools/FindThatBook.Eval -- run --set golden --catalog replay
```

It replays the recorded golden pools in `tests/fixtures/golden`.

## Evaluation results

Each number comes from an `eval/results/<run>/summary.md` written by the eval tool (`tools/FindThatBook.Eval`, see
[eval/README.md](eval/README.md)). All runs are against **live Open Library**, on the final build (source fingerprint
`918d7b88`, pipeline binaries `bb794a4f`) unless a row says otherwise. Latency, call counts and cost for every run are
in [eval/RESULTS.md](eval/RESULTS.md).

Two things changed after these runs, and neither affects retrieval, ranking or status:
- The OpenAPI examples were re-recorded.
- The facts sent to the LLM for an exact-title match no longer list the title's words, because "matched terms about
  and boy" read badly.

The eval runs scored template explanations, not LLM-written ones.

**Which sets are in-sample:** golden, hard and topic were used while designing the rules, so they measure regressions,
not generalization. The **held-out** set was sealed until this build and run once per arm; nothing was tuned on it.

**How to read the columns:**
- top-1 / top-5 are measured over answerable rows.
- `match` n @ P is how many rows the app called `match`, and the share of those whose first result was right.
- Coverage is the share of answerable rows answered with `match`.
- Severe counts a `match` on the wrong book. Another record of the same book is not counted as severe.
- In every eval run the explanations used templates, so the LLM cost shown is for the interpretation step only.

**Held-out** (409 queries, sealed; Open Library errors: 1 row per arm)

| Interpreter | top-1 | top-5 | status | `match` @ P | coverage | severe | LLM $ per 1k searches |
|---|---|---|---|---|---|---|---|
| none (LLM off) | .727 | .813 | .895 | 227 @ .982 | .559 | 4 | 0 |
| gpt-4.1-mini | .823 | .884 | .927 | 300 @ .960 | .739 | 12 | 0.36 |
| claude-haiku-5-5 | .815 | .877 | .924 | 302 @ .944 | .744 | 16 | 0.19 |
| gpt-4.1-mini + judge (gpt-6-luna) | .820 | .884 | .932 | 282 @ .965 | .695 | 10 | 0.36 + 0.06 |

- **The interpreter is the biggest lever.** It adds about 10 points of top-1 and 18 points of coverage. Precision
  drops a little in exchange.
- **The judge** agreed with the code's first result on 322 of 337 rows. It moves precision from .960 to .965 and gives
  up 18 matches, which is why it stays off by default.
- **Gemini, the default, has no held-out run yet.** Its free tier allows 500 requests per model per day, and that
  allowance ran out during the topic run. This run is pending.

**In-sample regression sets**

| Set | Interpreter | top-1 | top-5 | status | `match` @ P | coverage |
|---|---|---|---|---|---|---|
| golden v2 (60) | none | .982 | .982 | .933 | 41 @ 1.000 | .719 |
| golden v2 | gemini-3.5-flash-lite (default) | 1.000 | 1.000 | 1.000 | 48 @ 1.000 | .842 |
| golden v2 | gpt-4.1-mini | 1.000 | 1.000 | .983 | 48 @ 1.000 | .842 |
| hard v1 (46) | none | .935 | .935 | n/a | 24 @ 1.000 | .522 |
| hard v1 | gpt-4.1-mini | 1.000 | 1.000 | n/a | 38 @ 1.000 | .826 |

These runs had no severe matches and no false matches. On golden, Gemini with prompt v5 gives the same result as v3
on every row.

**Topic requests** (topic v2: 24 topic requests, 13 real titles that contain topic words, and 1 topic plus author with
no hits; this is the dev set for the feature)

A topic row counts as correct when its status is `ambiguous`/`topic_search` and at least 3 of the top 5 results have a
subject heading that matches the topic.

| Interpreter | topic rows on topic | topic rows, right status | real-title controls | no-hit row |
|---|---|---|---|---|
| none | 17/24 | 18/24 | 13/13 (12 `match` @ 1.000) | no false match |
| gpt-4.1-mini | 21/24 | 21/24 | 13/13 | no false match |

- **Still missed with the LLM on:** "a mystery set in venice", "a cookbook for italian food" and "a biography of
  abraham lincoln". For the Lincoln request the books shown are about Lincoln, but they came from the broad search and
  carry no subject headings.
- **Gemini on this set is partial.** Its daily quota ran out partway through: 18 of the 21 rows it interpreted are on
  topic.

**Stability.**
- Retrieval: 20 golden queries were run 3 times each with caches bypassed. The top result, the top-5 set and the
  status were the same on 20 of 20 queries (final build).
- Gemini's interpretation: title and author were the same across runs on 18 of 20 queries, and the whole hypothesis on
  12 of 20. This was measured with prompt v3 (build `dd58f98d`); a re-measure on v5 is pending.

**Ranking rules (ablation).** I switched off each guarded rule in `MatchRanker` in turn and re-ran.
- **Live run:** 200 in-sample adversarial queries on live Open Library, build `b872851e`. That build is from before
  the last topic-ranking fixes, and the ablation was not re-run on the final build.
  - With all the added rules off, 5 more rows are right and 1 more row is a severe wrong match.
  - `RequiredTitleWords` is the main trade-off: turning it off fixes 3 rows but lets "valley wind manga volume 1" match
    the wrong record.
- **Mirror run:** 1,356 rows on the local Open Library mirror. The rules prevent 6 severe matches (`TinyRecord` 5,
  `Mashup` 1) but cost 46 rows. Most of that cost is because the mirror lacks the edition titles and alternate author
  names the rules read.

**Decision:** keep every rule. A wrong `match` is the costliest error this app can make. Each rule is a switch in
`RankerOptions`, so it can be retired once fresh live data shows it no longer prevents anything.

**Pending, waiting for Gemini's daily quota to reset:**
- held-out with Gemini;
- the 13 topic rows Gemini didn't finish;
- hard with Gemini;
- Gemini interpretation stability on prompt v5.

## Known misses

Measured on this build (details per row in `eval/results/`; the edge-case ledger is [docs/FINDINGS-STATUS.md](docs/FINDINGS-STATUS.md)):

| Query | What happens | Why |
|---|---|---|
| `mark huckleberry`, `ahab whale` (LLM off) | right book #1, but `ambiguous` | without the interpreter, a first name or a character word is weak evidence; with Gemini both are `match` |
| `austen bennet` (LLM off; also with the judge on) | right book #1, `ambiguous` | a surname plus a character name: the judge declines to confirm it |
| `holden caulfield` (LLM off) | `none` | the only title evidence for *The Catcher in the Rye* in Open Library's data is an Italian edition title; a book literally titled *Holden Caulfield* is a study, not the novel. With the LLM on it is a `match` |
| `the hobbit tolkien dixon` with Claude Haiku as interpreter | Tolkien's original first, the Dixon graphic novel #2 | the interpreter's reading changes which variant is searched; Gemini and GPT-4.1-mini get it right |
| `hermione`, `katniss` | always `ambiguous` (*Philosopher's Stone* / *The Hunger Games* first) | by design: one character word never claims a single book, even when the judge is confident |
| Split records (e.g. *The Book of Mormon* across many 8–19-edition works with "Joseph Smith" vs "Joseph Smith, Jr.") | the first record shown is close to arbitrary | Open Library data; author names with "Jr." are kept distinct on purpose (Dumas père vs fils) |
| `first_publish_year` | wrong for some classics (*A Tale of Two Cities* = 1800) | Open Library data; shown, never filtered on, never asserted in explanations |
| `books like harry potter`, `un livre sur les ours`, `teach me about volcanoes`, `a mystery set in venice` (LLM off) | the Harry Potter books (`ambiguous`), or `none` | the code's request rules handle only plain English frames ("a book / books about X"); similarity requests, other languages and genre wording need the interpreter |
| `a mystery set in venice`, `a cookbook for italian food`, `a biography of abraham lincoln` (gpt-4.1-mini) | Venice: `none`. Cookbook: Italian cookbooks, but treated as a title search (`near_title_author`) rather than `topic_search`. Lincoln: books titled *Abraham Lincoln*, `ambiguous` (`weak_evidence`) | Venice: Open Library has no single heading for a genre plus a setting. Cookbook: the word "cookbook" matched titles first. Lincoln: the books came from the broad search with no subject headings, so the code can't show they are on topic |


## How I used AI tools

<!-- Andrew: please review and edit this section in your own words before submitting. -->

- **Research before code.** Python prototypes over a local Open Library mirror (41.7M works, built from the public
  dumps) and the live API, plus measured experiments: LLM-as-interpreter vs LLM-alone vs rules, ranking variants,
  2,886 OpenAI Decisions calls to calibrate the judge, and three rounds of adversarial queries generated with the Codex
  CLI. The rules in `MatchRanker` and the defaults in `appsettings.json` come from those measurements.
- **The C# build** was done with Claude Code orchestrating specialist agents with strict file ownership (normalization,
  matching, Open Library, AI adapters, explanations, API, suggest, eval, traceability) against shared contracts. Two
  independent reviewer agents and a Codex review audited the result; their findings (a throttle-vs-circuit-breaker
  burst failure, hidden Unicode tag characters reaching prompts, a crash on Arabic-Indic digits, honest call counting,
  a CSRF-able admin probe) were fixed with regression tests.
- **What I verified by hand:** the matching rules against the research results, every package version, the provider
  smoke calls, live runs of the brief's examples, and that every number in this README comes from a committed
  `eval/results/*/summary.md` that names the build it ran on.
- **What I rejected:** an LLM re-ranker (±2 points, and it broke the brief's Dixon case), embeddings (−9 as a re-rank
  signal), fancier string similarity (≈ 0), and an LLM explanation that sees the raw query (it repeated injected text).


## What I'd do with more time

1. Deploy a live demo (the container is ready: `docker compose up --build`) with a paid Gemini key or a stricter per-visitor budget.
2. Re-measure the ranking rules on fresh adversarial rounds against live Open Library (the battle-test rounds used a
   local mirror that lacks some live fields), and retire any rule whose ablation shows no effect.
3. ISBN, Open Library id and URL routing (`9780261102217`, `OL27482W`) as direct lookups.
4. Prefer a Latin-script author name when the query is Latin (the canonical *三体* record lists 刘慈欣 first).
5. Export the per-request measurements as OpenTelemetry (the same numbers `meta` reports) and add a distributed cache
   for more than one instance.
6. Stream or defer the explanation call so the ranked list can render before the sentences arrive.


## Docs map

- [docs/API.md](docs/API.md): frontend contract · [docs/adr/](docs/adr/): 7 short ADRs · [eval/README.md](eval/README.md): datasets and how to run the eval
- [docs/TRACEABILITY.md](docs/TRACEABILITY.md) · [docs/FINDINGS-STATUS.md](docs/FINDINGS-STATUS.md) · [docs/ai-smoke.md](docs/ai-smoke.md)
- Frontend: [docs/FRONTEND.md](docs/FRONTEND.md) · [docs/DESIGN.md](docs/DESIGN.md) · [docs/RUNNING.md](docs/RUNNING.md) (full cross-platform run guide)
- Production: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) (Fly.io release and verification)

## Frontend

A small React app in `web/`: one search box, readable results, a "Search activity" trace of what the API measured,
local search history and a local-only Admin page. It shows the API's decisions as they are: server order, `match` /
`ambiguous` / `none` (and "Books about …" for topic requests), primary authors apart from contributors, and the
grounded explanation for each book. It never
re-ranks, guesses confidence or invents a year.

**Stack:** React 19, TypeScript, Vite, Tailwind CSS 4, shadcn/ui on Base UI, TanStack Query and Router, Vitest and
Playwright. npm workspaces, one lockfile at the repo root.

| Command (repo root) | What it does |
|---|---|
| `npm run dev` | Installs dependencies if needed, then API (Development) + Vite with hot reload at http://localhost:5173 |
| `npm run setup` | Checks the machine and installs the web packages, the .NET 10 SDK if needed, and the NuGet packages, without starting the app |
| `npm start` | Builds the app into the API's `wwwroot` and serves both from http://localhost:5080 |
| `docker compose up --build` | Production image at http://localhost:8080 (Admin is off there by design) |
| `npm test` · `npm run test:e2e -w web` | Unit/component tests · browser smoke tests; both use recorded API examples, no backend or keys |
| `npm run check` | Typecheck, lint, unit tests and build (what CI runs for the web app) |

**How it talks to the API:** same origin everywhere (the Vite proxy in dev, `wwwroot` otherwise), so there is no CORS
setup. A search runs only when you ask: it is a TanStack mutation, so focus, reconnect, refresh, back/forward and
returning from Admin never re-run it; Stop aborts it. Finished searches are saved in this browser (newest 30) and
restored from History or the URL (`/?entry=…`) without calling the API. The TypeScript contract types are checked
against the API's OpenAPI document and its own example responses, which the UI tests also use as fixtures.

More: [docs/FRONTEND.md](docs/FRONTEND.md) (contract inventory, requirement → screen → test map, decisions, known
gaps) · [docs/DESIGN.md](docs/DESIGN.md) (design guide) · [docs/RUNNING.md](docs/RUNNING.md) (setup on every OS).
