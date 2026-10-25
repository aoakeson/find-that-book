# Frontend implementation note

The React app in `web/` is a thin, honest view over the API. **The backend decides** (matching, ranking, status,
authorship, explanations); the frontend displays those decisions without re-ranking, guessing confidence or
inventing data. Design rules: [DESIGN.md](DESIGN.md). API contract: [API.md](API.md). Setup on every OS:
[RUNNING.md](RUNNING.md).

Stack: React 19.3, TypeScript 6.0, Vite 8, Tailwind CSS 4, shadcn/ui on Base UI primitives, TanStack Query 5,
TanStack Router 1, Vitest 5 + Testing Library, Playwright + axe-core. One package manager (npm workspaces) and one
lockfile at the repo root. TypeScript stays on 6.0 because typescript-eslint supports `< 6.1`.

## 1. Contract inventory (what the working tree's API offers)

| Need | Endpoint (verified against `/openapi/v1.json`, the C# contracts and live calls) | Used by |
|---|---|---|
| Search | `GET /api/books/search?q=` (`limit` 1–10, default 5; the UI sends none, so the brief's "up to 5") → 200 for `match`, `ambiguous` and `none` | `features/search/session.tsx` |
| Topic requests | Same endpoint: `interpretation.intent` = `topic`, `interpretation.topics`, `status_reason` `topic_search`, `candidates[].matched_subjects` | `features/search/results` |
| Suggestions | `GET /api/suggest?prefix=&limit=6`: local index, no upstream call, empty prefix → `[]`, works from one character | `features/search/composer` |
| Health | `GET /api/health`: provider, `ai_configured`, environment; never calls Open Library or an LLM | `api/health.ts` (the "AI is off" note) |
| Admin status | `GET /api/admin/ai` (Development + direct loopback + same-origin only, else the JSON 404 `not_found`); not in the OpenAPI document | `features/admin` |
| Admin changes | `PUT /api/admin/ai` (`provider`: `auto`, a provider or `none`; `judge_enabled`), `PUT`/`DELETE /api/admin/ai/keys/{gemini\|anthropic\|openai\|judge}`, `POST /api/admin/ai/probe/{provider}`; in memory only | `features/admin` |
| Errors | RFC 9457 problem+json with stable `code`, `trace_id`, `errors` keyed `Q`/`Limit`/`Prefix`/`ApiKey`, `hint`; `Retry-After` on 429/503/504 | `api/client.ts` (`ApiError`) |
| Request identity | `X-Request-Id` = `meta.request_id` = `trace_id` | Search activity footnote, error references |
| Cancellation | the browser aborts the fetch (headers and body); the API observes `RequestAborted` | `session.cancel()` |
| Progress | **none**: one GET returns everything at the end; `meta` carries measured per-stage timings, catalog steps and LLM calls | Search activity (after completion only) |

Historical notes disagreed on several points; the working tree settles them: search is **GET** (not POST); validation
errors are keyed **`Q`** (C# member name); stop-word-only input is a **400 `no_searchable_terms`** ("Add a title or
author."); a refused query is a 200 `none` (the old `query_rejected` code is gone); short and non-Latin queries (`it`,
`三体`) are valid; suggest answers from the first character, with a ~150 ms debounce recommended.

`web/src/api/types.ts` mirrors the contract by hand. `web/src/api/contract.test.ts` compares every field name,
nullability and enum value with a saved OpenAPI snapshot (`npm run api:snapshot -w web` refreshes it from a running
API), the admin records in `AdminContracts.cs` (admin routes are not in the OpenAPI document), and the API's own
example files (`src/FindThatBook.Api/OpenApi/Examples/*.json`). The UI tests use those example files as fixtures, and
the backend's drift test deserializes the same files into its C# types, so both sides are pinned to one set of bytes.
The check caught three real contract changes during the build (`query_rejected` removed, `slow_empty_requeries` and
`provider_selection` added).

## 2. Requirement → screen → code → test

| Requirement (brief / frontend guide) | Screen behaviour | Code | Tests |
|---|---|---|---|
| Messy plain-text query | One composer, free text, any script, IME-safe Enter, examples from the brief fill (never search). "More things you can search for" opens the rest, grouped, with Needs AI on the ones that require a provider | `composer/Composer.tsx`, `ExampleQueries.tsx`, `example-catalog.ts`, `components/ui/autocomplete.tsx` | `composer/Composer.test.tsx`, `ExampleQueries.test.tsx`, e2e `search.spec.ts` |
| Autocomplete | `/api/suggest` only, 150 ms debounce, superseded requests aborted, late answers dropped, the last list stays (busy) until the new one arrives, failures silent | `composer/use-suggestions.ts` | `composer/Composer.test.tsx` |
| Ordered list, up to 5, server order | Rendered exactly as returned; no re-ranking or confidence | `results/ResultList.tsx` | `results/ResultsRegion.test.tsx` (every API example) |
| match / ambiguous / none | "Best match" + "Other possibilities" · "A few books fit your clues" + hint · "No book matched" + hint | `results/ResultList.tsx` | same, parameterised |
| Topic requests | "Books about bears" + hint, equal cards (never one best match), matched subjects as chips; "No books found about bears" | `results/ResultList.tsx`, `Candidate.tsx` | `results/ResultsRegion.test.tsx` (topic requests) |
| Required fields | title, primary author(s), first publish year only when present, Open Library link, cover or a stable placeholder, explanation | `results/Candidate.tsx`, `BookCover.tsx` | `results/ResultsRegion.test.tsx` |
| Primary authors vs contributors | Author line from `author`; contributors as quieter "Also credited" credits with roles | `results/Candidate.tsx` | same |
| Degraded answers | Quiet note per `degraded_reasons`, causes only as reported; results stay | `results/degraded.ts`, `Notices.tsx` | `results/*.test.tsx` |
| AI interprets, every step measured | "Search activity" built only from `meta` + `interpretation` after the response | `results/activity.ts`, `SearchActivity.tsx` | `results/ResultsRegionView.test.tsx` |
| Cancel | "Stop" aborts (headers and body); "Search stopped" is neutral; late responses ignored | `session.tsx`, `api/client.ts` | `session.test.tsx`, `api/client.test.ts` |
| Errors | Inline field error for 400s; Alert + explicit "Try again" for 5xx/network; `Retry-After` honoured on 429/503/504 (button and new searches wait, neutral countdown); never shown as "no match" | `session.tsx`, `results/failure.ts`, `Composer.tsx` | `session.test.tsx`, `results/ResultsRegionView.test.tsx`, `composer/Composer.test.tsx`, e2e |
| History | Sheet; 30 newest searches in this browser; restore without an API call; delete; clear with confirmation; safe when storage is missing, full, corrupt, older or changed in another tab | `features/history/*` | `history/storage.test.ts`, `HistoryButton.test.tsx`, e2e `history.spec.ts` |
| Routing | `/` (`?entry=` = local history id), `/admin` (lazy), designed 404, error boundary; back/forward/refresh/return from Admin never re-run a search | `router.tsx`, `app/*` | `app/routing.test.tsx`, `session.test.tsx` |
| Keys, the easy way | Runs without a key; the home page says AI is off and how to turn it on: `.env` or the Admin page; Admin's **Turn on AI** offers both | `search/AiOffNote.tsx`, `admin/AiProviderSection.tsx` | `search/AiOffNote.test.tsx`, `admin/TurnOnAi.test.tsx` |
| Admin | Provider (incl. automatic), keys (write-only), test connection, second opinion; unavailable state outside Development/loopback | `features/admin/*` | `admin/AdminPage.test.tsx`, `AdminKeys.test.tsx`, `TurnOnAi.test.tsx`, e2e `pages.spec.ts` |
| Accessibility, responsive, motion | See DESIGN.md | `index.css`, `app/*` | `lib/motion.test.tsx`, e2e `a11y.spec.ts` (axe, WCAG 2.2 AA), `mobile.spec.ts`, `pages.spec.ts` |

## 3. Decisions

- **Search is a TanStack mutation, not a query.** A paid search must never re-run on focus, reconnect, remount, a
  StrictMode double render or link preloading. Each run has an id and its own `AbortController`; a newer run, Stop or
  a history restore aborts the older one, and its late response is ignored. Retries are explicit only (the API already
  retries Open Library; stacked retries multiply LLM calls), and offline submits fail at once instead of queueing until
  the connection returns. A `Retry-After` is one fixed deadline shared by "Try again" and new searches.
- **One view at a time** (`SearchView`: home, pending, success, failed, cancelled, restored, missing-entry). Every result
  view carries the query it belongs to; a new search replaces the results area with its pending state, so an old answer
  is never shown under a new query. The draft is separate from the submitted query.
- **History drives the URL.** A completed search is saved and pushed as `/?entry=<local id>` (a repeated query keeps
  its id). Refresh, back/forward, returning from Admin and picking from History restore the saved snapshot without
  calling the API. Only a *change* of `?entry=` counts as navigation: coming back to the same URL (browser Back from
  Admin) keeps what is on screen, including a search still running. The id means nothing on another device, which
  the missing-entry screen says plainly.
- **History stores an allowlisted snapshot only** (query, time, status, hint, degraded reasons, topics, displayed
  candidate fields). No `meta`, prompts, provider data or keys. Versioned key `ftb.history`, newest 30, re-read before
  every write so a change made in another tab is kept (two tabs writing at the same instant are last-writer-wins:
  localStorage has no lock).
- **Autocomplete uses Base UI Autocomplete**, styled like the shadcn Combobox. shadcn's Combobox (Base UI Combobox)
  requires picking an item; Base UI documents Autocomplete as the free-text widget with the same ARIA combobox keyboard
  model. Accepting a suggestion fills the composer with "title author" and does not search: a search spends LLM
  calls, so the user confirms with Enter (docs/API.md suggests searching on select; this is a deliberate UI choice).
- **No fake progress.** The API has no progress transport, so while a search runs the activity line shows the animated
  book, "Searching…" and an elapsed counter, nothing else. The measured steps appear when the response arrives.
- **Keys either way.** Without a key the app still searches. A key can go in `.env` (kept across restarts, used by
  Docker) or be pasted on the Admin page (works at once, lasts until the API stops, local Development only). The
  API's automatic provider selection uses the first provider with a key; with an explicitly chosen provider that has
  no key, the first key added switches searches to its provider.
- **Admin keys are write-only and transient.** The key lives only in the password input's DOM value, leaves the page
  in one request body, never enters React state, TanStack caches, URLs, storage, logs or toasts, and is cleared on
  success, Cancel and unmount. Status-changing admin calls run one after another (a shared mutation scope), so a slow
  earlier answer can't overwrite a newer one.
- **Same origin everywhere.** Dev: Vite proxies `/api` to the API without `X-Forwarded-*`. Production-like: the API
  serves the build from `wwwroot`. No CORS configuration. Because the proxy makes every caller look local, it repeats
  the admin rule itself (loopback socket, same `Origin`); the API also refuses cross-site admin calls.
- **Small initial download.** Admin is a lazy route; React, Base UI and other libraries are split into long-cached
  chunks (main app chunk ≈ 28 kB gzipped).

## 4. Gaps and limitations (honest list)

- **No live progress.** A small integration if wanted later: an opt-in `text/event-stream` variant of the search
  endpoint emitting the stages `SearchService` already times (`interpret`, each catalog step, `rank`, `explain`,
  `judge`), then the normal JSON body. The UI's Trace already renders those stages.
- **Cancel stops waiting, not necessarily spending.** Aborting cancels the API's pipeline token, but an LLM call
  already in flight may still be billed upstream.
- **Admin exists only in Development on the API's own machine.** In Docker/Production it shows an "unavailable" state
  by design (no authentication is in scope; a hidden link is not access control).
- **Screen readers and the open suggestion list.** Base UI 1.9 hides the rest of the page from assistive technology
  while the list is open (axe `aria-hidden-focus`); keyboard use is unaffected because leaving the field closes the
  list. Documented in `components/ui/autocomplete.tsx`; no supported option changes it.
- **Password managers** may offer to save the API key field; declining keeps it out. `autoComplete="off"` and the
  1Password/LastPass opt-outs are set, but browsers decide.
- **Free-tier Gemini** (15 requests/min, 2 per search) returns `degraded` answers with template explanations during
  bursts; the UI shows them as valid answers with a quiet note.
- **Open Library data** shows through as returned: wrong first-publish years on some classics, odd contributor roles.
- **Verified by emulation, not on a device:** phone widths, touch and landscape were checked with Playwright
  emulation; the software keyboard on a real phone and Windows itself were not run (the `web.yml` CI job runs
  `npm run doctor` and `npm run smoke` on `windows-latest`).

## 5. Verification (2026-10-25)

| Check | Result |
|---|---|
| `npm run check` (typecheck, lint, format, unit/component tests, build) | Green; the unit suite runs every API example file, including `topic_search` and `topic_none` |
| `npm run test:e2e -w web` (Playwright, every `/api` call mocked from the API's examples; axe WCAG 2.2 AA on home, results with activity open, History, Admin, 404) | 13/13, stable over repeated runs |
| Live smoke against the local API (Development, Gemini) | Through `npm run dev` itself (02:42 MT): banner "AI on (gemini)"; `/api/health` 200; suggest "har" 200 from the real index; `GET /api/admin/ai` 200 (`provider_selection: auto`); `/api/nope` → JSON 404; "mark huckleberry" → Best match, Adventures of Huckleberry Finn; Admin then Back made no extra request; Ctrl+C left no listeners. Earlier runs: "tolkien hobbit illustrated deluxe 1937" → match, The Hobbit (2 LLM calls, 1 Open Library call, 2.6 s); "find me a book about bears" → topic_search, 5 books with matched subjects; history restore, Back and return from Admin made 0 extra requests; a disposable second-opinion key set and removed through the proxy; `/nope` → 404 page. 4 paid searches in total |
| `npm run smoke` (runs `npm start` and `npm run dev` for real, checks pages, deep links, JSON 404 for unknown `/api`, clean Ctrl+C, no leftover listeners) | macOS 18/18; Linux (Docker, fresh clone, Node 24, .NET 10.0.402) 18/18 plus the whole unit suite |
| `docker compose` image | Builds; serves the app, deep links and `/api` in Production; unknown `/api` and `/api/admin` → JSON 404 |
| Not run here | Real Windows (the `web.yml` `run-scripts` job runs doctor and smoke on `windows-latest`), a real phone's software keyboard, real screen readers |

## 6. Backend files changed by the frontend work

- `.gitignore`: added `node_modules/` (npm workspaces install at the repo root).
- `README.md`: only the sections the backend lead reserved for the frontend (the run block near the top, the key
  table, and "## Frontend").
- Nothing in `src/`, `tests/`, `tools/` or `eval/` was edited by the frontend work. Backend changes it prompted (the
  cross-site admin guard, automatic provider selection, placeholder contributor names) were made by the backend
  session.
