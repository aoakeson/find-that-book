# Find That Book API: frontend contract

The live, complete contract is the OpenAPI 3.1 document at `/openapi/v1.json`, browsable with examples at `/scalar`
(both on in every environment). This page is the short version for building a client.

- JSON is **snake_case**; enums are **snake_case strings**. Every field is always present; unknown values are `null`.
- Every response carries `X-Request-Id`. The same value is `meta.request_id` on a search and `trace_id` on an error.
- Live: <https://books.andrewoakeson.com/scalar> (Production). Run locally: `dotnet run --project src/FindThatBook.Api` →
  <http://localhost:5080/scalar>.

## GET /api/books/search?q=&limit=

| Parameter | Rules |
|---|---|
| `q` (required) | 1–300 UTF-16 code units after trimming and removing control/zero-width characters; at least one letter or digit (any script: `三体`, `it`, `v.` are valid); not only stop words; given once |
| `limit` | whole number 1–10, default 5 |

**200** for every answer, including "nothing found":

| `status` | Meaning | UI |
|---|---|---|
| `match` | One confident answer: the top candidate is tier `a`/`b` and clearly ahead | Show the first card prominently |
| `ambiguous` | Several plausible works, in order | Show the list and `hint` |
| `ambiguous` + reason `topic_search` | A topic request: books about `interpretation.topics` | "Books about …" heading + list (see Topic requests) |
| `none` | Nothing usable; `candidates` is `[]` | Show `hint` |

`degraded: true` means a lower-quality but valid answer; `degraded_reasons` says why: `ai_not_configured`,
`ai_budget_exhausted`, `interpretation_fallback`, `explanation_fallback` (template sentences), `catalog_partial`
(a later Open Library call failed), `deadline_reached`. The LLM never causes an error status.

```jsonc
// GET /api/books/search?q=hobbit%20dixon (recorded on build 918d7b88 with gpt-4.1-mini, judge off; with Gemini, the default, the meta.llm provider/model differ and the LLM-written parts may too; the current build sends explain.v5; first candidate shown)
{
  "query": "hobbit dixon",
  "status": "match",   // match | ambiguous | none
  "status_reason": "exact_title_contributor_author",
  "hint": null,   // set for ambiguous / none
  "degraded": false,
  "degraded_reasons": [],
  "interpretation": {   // how the text was read; it only steers the search
    "title": "The Hobbit",
    "author": "J.R.R. Tolkien",
    "characters": [],
    "keywords": [
      "hobbit"
    ],
    "english_title": null,
    "original_title": null,
    "year": null,   // from the query text (cleaner): a soft bonus, never a filter
    "removed_noise": [],
    "source": "llm",   // llm | heuristic
    "intent": "book",   // book | topic (see Topic requests)
    "topics": []   // the subject terms searched for a topic request
  },
  "candidates": [
    {
      "rank": 1,
      "work_id": "OL219602W",
      "title": "The Hobbit",   // brief field
      "subtitle": null,
      "author": "J.R.R. Tolkien",   // brief field: primary authors (≤ 3 names)
      "primary_authors": [
        {
          "key": "OL26320A",
          "name": "J.R.R. Tolkien",
          "url": "https://openlibrary.org/authors/OL26320A"
        }
      ],
      "primary_authors_total": 1,   // primary_authors is capped at 3 (the matched author is always kept)
      "contributors": [
        {
          "key": "OL2453782A",
          "name": "Charles Dixon",
          "role": "adapter"
        },
        {
          "key": "OL758306A",
          "name": "Sean Deming",
          "role": "adapter"
        },
        {
          "key": null,
          "name": "Donato Giancola",
          "role": "illustrator"
        }
      ],
      "contributors_total": 4,   // contributors capped at 3 (the matched one is always kept)
      "first_publish_year": 1990,   // brief field; as Open Library lists it (null when absent or implausible)
      "edition_count": 10,
      "open_library_url": "https://openlibrary.org/works/OL219602W?edition=key%3A/books/OL22039557M",   // the work page, opened on the edition the search matched
      "cover_url": "https://covers.openlibrary.org/b/id/8406766-M.jpg",   // null when there is no cover
      "match_tier": "b",   // a | b | c | d | e
      "match_tier_label": "Exact title + contributor-only author",
      "matched_subjects": [],   // topic requests: ≤ 3 of the work's Open Library subjects that matched
      "explanation": "Exact title match; J.R.R. Tolkien is the primary author; Charles Dixon is listed as adapter; Charles Dixon is the matched contributor; 10 editions.",
      "explanation_source": "llm",   // llm | template (grounding check failed or the LLM was unavailable)
      "second_opinion": false,   // true on the judge's pick when it disagreed with rank 1
      "duplicates_folded": 1
    }
  ],
  "meta": {
    "request_id": "00-a92809e3e265c6af67110fa0e1395821-22c9173f9592f1ec-00",
    "elapsed_ms": 2651,
    "timings_ms": {
      "clean": 0,
      "interpret": 861,
      "rank": 37,
      "retrieve": 899,
      "judge": 0,
      "explain": 1751
    },
    "open_library": {
      "calls": 1,
      "cache_hits": 0,
      "upstream_requests": 1,
      "retries": 0,
      "slow_empty_requeries": 0,
      "budget": 5,
      "steps": [
        {
          "step": "broad",
          "outcome": "ok",
          "cache_hit": false,
          "attempts": 1,
          "elapsed_ms": 429,
          "results": 9,
          "num_found": 9
        }
      ]
    },
    "llm": {
      "provider": "openai",
      "model": "gpt-4.1-mini",
      "calls": 2,
      "cache_hits": 0,
      "input_tokens": 1547,
      "output_tokens": 221,
      "est_cost_usd": 0.0009724,
      "prompt_versions": {
        "interpret": "interpret.v5",
        "explain": "explain.v4"
      },
      "steps": [
        {
          "operation": "interpret",
          "provider": "openai",
          "model": "gpt-4.1-mini",
          "prompt_id": "interpret.v5",
          "status": "ok",
          "cache_hit": false,
          "elapsed_ms": 861,
          "input_tokens": 668,
          "output_tokens": 51
        },
        {
          "operation": "explain",
          "provider": "openai",
          "model": "gpt-4.1-mini",
          "prompt_id": "explain.v4",
          "status": "ok",
          "cache_hit": false,
          "elapsed_ms": 1747,
          "input_tokens": 879,
          "output_tokens": 170
        }
      ]
    },
    "judge": {   // off by default
      "enabled": false,
      "status": "off",
      "pick": null,
      "probability": null,
      "threshold": 0.9,
      "agreed": null,
      "model": null,
      "latency_ms": null,
      "input_tokens": null,
      "est_cost_usd": null,
      "detail": null
    }
  }
}
```

### Topic requests

A sentence asking for *any* book about something ("find me a book about bears", "picture books about trains") is a
topic request: `interpretation.intent` is `"topic"` and `interpretation.topics` holds the subject terms searched (from
the LLM, or the words after "a book about …" when the LLM is off). The answer is a list of books about the topic:

- `status` is `ambiguous` with `status_reason: "topic_search"` and the hint "Add a title or author to find a specific
  one." It is never `match`, and the judge doesn't run. Nothing about the topic → `none`, `status_reason:
  "no_candidates"`, `candidates: []`, hint "Try a broader topic, or add a title or author."
- Candidates are usually tier `e` (no title/author evidence); each carries `matched_subjects` (≤ 3 Open Library
  subject headings that contain a topic word) and an explanation that cites them.
- Scalar has two live examples: `topic_search` ("find me a book about bears") and `topic_none` ("a book about xqzv").
- **UI:** show a "Books about {topics}" heading over the list and the hint; never render a single "best match" card for
  a topic request. `matched_subjects` work well as chips on each card.
- A title that happens to contain request or topic words ("About a Boy", "Looking for Alaska", "Find Me") is still a
  book request (`intent: "book"`).

**Tiers** (the brief's hierarchy; labels, the score usually orders the list, with typed exact primary titles of at least 10 editions promoted above near-title and author-only hits): `a` exact title + primary author (or exact
title, no author typed) · `b` exact title + contributor-only author · `c` near-match title + author · `d` author only
(the author's most-published works) · `e` no clear winner. Only `a`/`b` can be a `match`.

**meta numbers are measured, never estimated** (except `est_cost_usd`, a paid-tier estimate even on a free tier).
`null` = not known: e.g. tokens when a provider request reported no usage, cost for a model missing from the price
table. `llm.calls` counts provider requests (cache hits and calls skipped for no key/budget are excluded; a 429
costs nothing). `open_library.retries` counts requests repeated after a 408/429/5xx; the one lighter request sent after
an attempt timed out or answered 0 hits slowly is counted in `slow_empty_requeries`, not as a retry. `judge.status`: `off` (switched off, the default; `enabled: false`) · `skipped` (on, but not meaningful for this ranking, e.g. author-only or none) · `agreed` ·
`disagreed` · `chose_none` · `refused` · `unavailable`. The judge only gates `status`; it never reorders.
When the judge is enabled, a badge could read "Second opinion agreed (0.99)" on `agreed`, or "Second opinion disagreed:
see ★" on the `second_opinion` card.

## GET /api/suggest?prefix=&limit=8

Local index of real works, no upstream calls; `Cache-Control: public, max-age=3600`. Debounce ~150 ms; on select, run
a normal search with the chosen title + author.

```json
{ "prefix": "har", "suggestions": [{ "work_id": "OL82563W", "title": "Harry Potter and the Philosopher's Stone",
  "author": "J. K. Rowling", "first_publish_year": 1997, "cover_url": "https://covers.openlibrary.org/b/id/10521536-M.jpg",
  "edition_count": 337, "open_library_url": "https://openlibrary.org/works/OL82563W", "match": "title" }] }
```

`prefix` 0–100 characters (empty → `200` with `[]`: a typing state, not an error); `limit` 1–10. `match`: `title`
(whole title starts with the prefix) · `word` (a title word does) · `author` (the author's surname does).

## GET /api/health

Never calls Open Library or an LLM. `{ "status": "ok", "version", "environment", "ai_provider", "ai_configured",
"judge_enabled", "suggest_index": { "version", "count" }, "upstream" }`. `ai_configured: false` → expect `degraded`
answers (heuristic parse + template sentences). `upstream` reports what this process has seen, without details that
could leak a query or key: `open_library_circuit` (`closed`, `open`, `half_open`), `llm_status` (`ok`, `cooldown`
after a 429, `error`; `null` before any LLM call), and the time and short reason of the last failure for each
(`open_library_last_failure`, `…_reason`, `llm_last_failure`, `…_reason`).

## Errors (RFC 9457, `application/problem+json`, whatever the `Accept` header)

Every error has `type` (`/problems/<code>`), `title`, `status`, `detail`, a stable **`code`** and **`trace_id`**.
Branch on `code`, not on text.

| Status | `code` | When | Retry |
|---|---|---|---|
| 400 | `validation_failed` | `errors` holds field messages keyed **`Q`**, **`Limit`**, **`Prefix`** (C# member names) | fix input |
| 400 | `no_searchable_terms` | only stop words (`the`, `a of the`); `hint`: "Add a title or author." | fix input |
| 404 | `not_found` | unknown `/api/*` route (never the SPA's HTML) | no |
| 405 | `method_not_allowed` | known route, wrong method; `Allow` header | no |
| 429 | `rate_limited` | over a limit; **`Retry-After`** seconds | after Retry-After |
| 502 | `catalog_bad_response` | Open Library sent something unusable on the first call | later |
| 503 | `catalog_unavailable` | Open Library down / circuit open; **`Retry-After`** (Open Library's, or 15) | after Retry-After |
| 504 | `catalog_timeout` / `search_timeout` | Open Library timed out / the 25 s deadline passed with nothing in hand | yes |
| 500 | `internal_error` | a bug; no details outside Development | report `trace_id` |

```json
{ "type": "/problems/validation-failed", "title": "One or more validation errors occurred.", "status": 400,
  "errors": { "Q": ["The query must be at most 300 characters."] }, "code": "validation_failed", "trace_id": "00-…-01" }
```

**Rate limits** (defaults, per minute): search 20 per client IP and 60 for everyone together, plus at most 6 running
at once with 16 more queued (a burst waits at the front door instead of overflowing Open Library's 3 req/s; beyond the
queue it is a 429); suggest 120 per client IP; health unlimited. Behind a reverse proxy the client IP comes from
`X-Forwarded-For` only when the proxy is listed in `ForwardedHeaders:KnownProxies`/`KnownNetworks` (none by default).

## Development-only admin API (`/api/admin/ai`)

For a local settings panel; deliberately left out of `/openapi/v1.json` (this page is its documentation). Mapped only
when `ASPNETCORE_ENVIRONMENT=Development`, and only answers direct loopback calls without proxy headers (`X-Forwarded-For`, `Forwarded`, `X-Real-IP`…) that no other website triggered
(`Sec-Fetch-Site` must be absent, `same-origin` or `none`; an `Origin` must equal the request's own scheme://host);
everything else gets the `not_found` 404. Same-origin pages (including through the Vite proxy) and curl/Scalar work.
A dev proxy that adds forwarding headers is refused (Vite's default proxy doesn't add them).
**Runtime changes live in memory and are lost on restart; `.env` and appsettings are never rewritten.**
Keys go in and never come out (status shows `configured`, `key_source`, and a `sha256:` fingerprint); keys are never logged.

| Call | Body | Returns |
|---|---|---|
| `GET /api/admin/ai` | | status: `active_provider`, `provider_selection`, `ai_configured`, `providers[{name, model, active, configured, key_source, key_fingerprint}]`, `judge{…}`, `persistence` |
| `PUT /api/admin/ai` | `{"provider": "auto\|gemini\|anthropic\|openai\|none", "models": {"gemini": "…"}, "judge_enabled": true}` (any subset) | status |
| `PUT /api/admin/ai/keys/{gemini\|anthropic\|openai\|judge}` | `{"api_key": "…"}` (8–512 chars, no spaces) | status |
| `DELETE /api/admin/ai/keys/{provider}` | | status (the configured key, if any, applies again) |
| `POST /api/admin/ai/probe/{gemini\|anthropic\|openai}` | | `{provider, model, ok, status, elapsed_ms, input_tokens, output_tokens, detail}`: one tiny real call (costs a few tokens) |

**Provider selection.** `provider_selection` is `"auto"` or `"explicit"`. With `"auto"` (set by `PUT {"provider": "auto"}`)
the first provider with a key wins, in the order gemini → anthropic → openai; `"explicit"` means a named provider (from
configuration or this API). `active_provider` is always the *resolved* provider (`"none"` when nothing has a key, which
means heuristic parsing and template explanations), and `providers[].active` follows it. `auto` is a selection mode, not
a provider: the key and probe routes reject it (400, `errors.Provider`).

`key_source`: `runtime` (set through this API) · `environment` · `dotenv` · `user_secrets` · `appsettings` · `none`.
