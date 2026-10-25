# AI smoke tests

Live calls through the real adapters. Each work stream appends its own section; do not overwrite other sections.

## Providers and the interpreter (prompt `interpret.v3`)

2026-10-24, stream A5. Test: `tests/FindThatBook.Infrastructure.Tests/Ai/LiveSmokeTests.cs` (explicit, skipped unless
`FTB_LIVE_SMOKE=1`). Path: `LlmQueryInterpreter` → `StructuredLlm` (cache off, budget off) → `ChatClientFactory` (official
SDK, hidden retries off) → provider. Keys came from `.env` via the process environment and were never printed. One
JSON line per call (status, finish reason, latency, tokens, parsed hypothesis), no raw responses.

```
set -a; source .env; set +a
FTB_LIVE_SMOKE=1 FTB_SMOKE_PROVIDER=gemini FTB_SMOKE_QUERIES=6 FTB_SMOKE_OUT=smoke.jsonl \
  dotnet test --project tests/FindThatBook.Infrastructure.Tests -- --explicit only --filter-class '*LiveSmokeTests'
# FTB_SMOKE_MODEL=<id> overrides the model, FTB_SMOKE_SKIP=<n> skips queries, FTB_SMOKE_PROVIDER=judge runs the judge
```

Queries (golden ids): `tolkien hobbit illustrated deluxe 1937` (brief-03), `mark huckleberry` (brief-01),
`great expecttions dikens` (typo-02), `scout atticus` (character-01), `hermione` (ambig-03), `the three body problem` (DE-06).
Latency is wall time of the whole adapter call; the first call of each test process includes TLS setup.

| Provider / model | Options sent | Calls | Ok | Finish | Latency (p50, range) | Tokens in / out | Supported |
|---|---|---|---|---|---|---|---|
| gemini / **gemini-3.5-flash-lite** | thinkingLevel MINIMAL, no temperature, autoContinuation off | 6 | **6** | stop ×6 | **0.80 s** (0.72–1.31) | 237–245 / 65–90, 0 thinking | **yes (default)** |
| gemini / gemini-3.8-flash | MINIMAL → HTTP 400 "Thinking level MINIMAL is not supported for this model"; then LOW | 1 + 6 | 0 | — | 6/6 hit the 4 s interpret timeout | — | **no** (today) |
| anthropic / **claude-haiku-5-5** | thinking disabled, effort low, no temperature, max_tokens 1024 | 6 | **6** | stop ×6 | 1.29 s (1.00–2.58) | 736–752 / 55–92 | **yes** |
| openai / **gpt-4.1-mini** | temperature 0, strict schema, max 512 | 5 | **5** | stop ×5 | 1.13 s (0.85–2.05) | 324–330 / 28–73, 0 reasoning | **yes** |
| openai / gpt-5-nano | reasoning_effort minimal, no temperature, max 512 | 1 | 1 | stop | 1.86 s | 323 / 103, 0 reasoning | works, not recommended |

Parsed hypotheses (title / author; characters as returned, before Core sanitizing):

| Query | gemini-3.5-flash-lite | claude-haiku-5-5 | gpt-4.1-mini |
|---|---|---|---|
| brief-03 hobbit deluxe 1937 | The Hobbit / J. R. R. Tolkien | The Hobbit / J.R.R. Tolkien | The Hobbit, or There and Back Again / J.R.R. Tolkien; **invented** Bilbo, Gandalf, Thorin |
| brief-01 mark huckleberry | Adventures of Huckleberry Finn / Mark Twain | Huckleberry Finn (english_title: Adventures of …) / Mark Twain | The Adventures of Huckleberry Finn / Mark Twain |
| typo-02 | Great Expectations / Charles Dickens | same | same |
| character-01 scout atticus | To Kill a Mockingbird / Harper Lee; Scout, Atticus | To Kill a Mockingbird / **null**; Scout, Atticus | To Kill a Mockingbird / Harper Lee; Scout Finch, Atticus Finch |
| ambig-03 hermione | **null title** (character Hermione) | guessed *Chamber of Secrets* / Rowling | guessed *Philosopher's Stone* / Rowling |
| DE-06 three body problem | The Three-Body Problem / Liu Cixin, original 三体 | same, original 三体 | (not run: 5-call budget) |

gpt-5-nano on `mark huckleberry`: title "The Adventures of Tom Sawyer, and The Adventures of Huckleberry Finn" (a mash-up) and
10 keywords. It no longer fails mechanically (AI-01: finish stop with 0 reasoning tokens at effort minimal), but the parse
is worse, so OpenAI's default stays the non-reasoning gpt-4.1-mini.

Gemini 3.8 flash: the 400 is now handled by `AI:Gemini:ThinkingLevelByModel` (gemini-3.8-flash → low). At LOW every call
ran past the 4 s interpret budget, and one direct untimed probe (same prompt and schema) had no answer after 60 s. Not
usable for interpretation today; re-measure before choosing it.

**Recommendation.** Default `gemini-3.5-flash-lite`: the only model under the ~1 s p50 target, 6/6 correct titles,
no thinking tokens, the safest answer on `hermione` (no guess, DE-01/AI-43), about $0.00027 per call at paid-tier prices
(free tier available). Fallbacks: `claude-haiku-5-5` (best value of the paid ones, ~$0.0001 per call, p50 1.3 s) and
`gpt-4.1-mini` (p50 1.1 s, ~$0.0002 per call, adds characters from its own knowledge, which Core now drops).

Guards verified offline against the real SDK request bodies (`ProviderOptionsTests`): one HTTP request per 429 for all
three SDKs (AI-10), no `temperature` for Anthropic or OpenAI reasoning models (AI-02), Gemini `thinkingLevel` MINIMAL and
`responseJsonSchema`, Anthropic `thinking.type=disabled` + `output_config.effort=low` + JSON schema format, OpenAI strict
`json_schema`.

**Measured rate limit (2026-10-25, live end-to-end run, dev key):** the Gemini key is free tier. Under load it answered
HTTP 429 `RESOURCE_EXHAUSTED`, quotaId `GenerateRequestsPerMinutePerProjectPerModel-FreeTier`, quotaValue **15**, RetryInfo
36–52 s. The cooldown skipped the LLM for that delay and searches degraded cleanly. Since then the budget is per provider:
`AI:Gemini:RequestsPerMinute` = 15 (the measured quota), Anthropic and OpenAI 50. Interpretation has priority:
`AI:Budget:ExplainReserve` = 3 keeps the last 3 permits of each minute for "interpret", so a busy minute falls back to
template explanations before it loses interpretation. The 429 warning now logs the quota id/value and the retry delay
(never the key or the request text).

## Decisions judge

Same test, `FTB_SMOKE_PROVIDER=judge`; request built by `DecisionsRequestBuilder` (choice + NONE, descriptions with year and
edition count), parsed by `DecisionsResponseParser`.

| Request (candidates in the order sent) | Status | Pick | p | Tokens | Latency |
|---|---|---|---|---|---|
| `hobbit dixon graphic novel`: Tolkien original, Dixon graphic novel | disagreed | OL219602W (the graphic novel) | 0.94 | 331 | 339 ms |
| `pride and prejudice`: 56-edition twin first, canonical second, Emma | disagreed | OL66554W (canonical) | **0.54** | 345 | 178 ms |
| `pride and prejudice`: canonical first (code order), twin, Emma | agreed | OL66554W | **0.98** | 345 | 389 ms |

AI-39: adding "4,042 editions" vs "56 editions" to the descriptions does not remove the order sensitivity (0.54 vs 0.98;
research 0.56 vs 0.99 without counts). What protects the status is code order (edition count first) plus folding twins
before the call, which the ranker does. Spend: 3 calls, ≈ $0.0001.

Total live spend for this section: Gemini 15 requests (6 flash-lite, 7 gemini-3.8-flash through the adapter, 2 direct
probes), Anthropic 6, OpenAI 6, judge 3; estimated ≈ $0.01 at paid-tier prices.

## Explanations

2026-10-24. Test: `tests/FindThatBook.Infrastructure.Tests/Explanations/ExplainLiveSmokeTests.cs` (explicit,
skipped unless `FTB_LIVE_EXPLAIN=1`). Path: `ExplanationFactsBuilder` (hand-built ranker output for golden-style queries)
→ `LlmExplanationWriter` (prompt `explain.v2`, facts JSON only, no raw query) → `StructuredLlm` (cache off) →
`GroundingCheck`. Keys came from `.env` via the environment and were never printed.

```
set -a; source .env; set +a
FTB_LIVE_EXPLAIN=1 FTB_SMOKE_PROVIDER=gemini FTB_SMOKE_BATCHES=4 FTB_SMOKE_OUT=explain.jsonl \
  dotnet test --project tests/FindThatBook.Infrastructure.Tests -- --explicit only --filter-class '*ExplainLiveSmokeTests'
# re-check recorded sentences against the current grounding rules, no calls:
FTB_LIVE_EXPLAIN=1 FTB_SMOKE_BATCHES=5 FTB_SMOKE_REPLAY=explain.jsonl dotnet test ... (same filter)
```

| Provider / model | Calls | Items | Grounding pass | Latency per call | Tokens in / out per call |
|---|---|---|---|---|---|
| gemini / gemini-3.5-flash-lite | 4 | 18 | **18 / 18** | 0.96–1.61 s | 725–806 / 158–306 |
| gemini / gemini-3.5-flash-lite, 10 items (AI-34) | 1 | 10 | **10 / 10** | 1.97 s | 1,279 / 473 (cap 1,024, no truncation) |
| anthropic / claude-haiku-5-5 | 2 | 9 | **9 / 9** | 2.29–2.84 s | 1,410–1,462 / 240–255 |

Total: 7 calls, 37 sentences, 37 grounded. All 27 sentences of the first runs still pass after the title-vocabulary
tightening (replayed offline). Batches covered every match kind except original-language title: exact (typed, edition
title DE-06, Cyrillic work + translator), interpreted, near, author-only, contributor-only (DA-019), character, title-only
(searched author not credited), weak, and the brief's tier-B case.

Examples (verbatim model output):

- gemini, tier B `hobbit dixon graphic novel`: "Exact title match; J.R.R. Tolkien is the primary author; Charles Dixon is listed as adapter; 10 editions."
- gemini, typed year matched: "Exact title match; J.R.R. Tolkien is the primary author; Alan Lee is listed as illustrator; matched term hobbit; Open Library's first publication year 1937 matches the year in the search; 482 editions."
- gemini, DE-06: "Exact title match, known in English as The Three-Body Problem; 刘慈欣 is the primary author; matched terms are three, body, problem; 45 editions."
- gemini, title-only: "Exact title, but not by the searched author; J.R.R. Tolkien is the primary author; 482 editions."
- anthropic, interpreted title: "Interpreted title match; Jane Austen is the primary author; matched character Elizabeth Bennet; 4042 editions."
- anthropic, contributor-only: "Contributor-only match; Roald Dahl is the primary author; Quentin Blake is listed as illustrator; 300 editions."

Observations:

- Both models used the code's `match_phrase` verbatim every time (AI-32) and named contributors only with their role.
- Neither stated a year except where the facts carried one (typed year matched, DA-061). The A Tale of
  Two Cities item (Open Library year 1800) got no year.
- Both skipped the optional `author_also_listed_as` (pen name). The template states it; the LLM sentence is still grounded.
- Style is terser than the template ("matched term hobbit", "4042 editions" without a thousands separator); both pass.
- Injection was not re-tested live: the explain prompt has no user text to inject. The recorded AI-29/AI-31 attack
  sentences are unit-test data and are all rejected (`GroundingCheckTests`).

### Revision 2: prompt `explain.v3` (2026-10-25, after a live end-to-end review)

Fixes for problems seen in live end-to-end responses: (1) sentences listed narrators, translators and editors of some
edition; the facts now carry at most 2 contributors, and only the one the query matched or the adapters of a tier A–C
work (none for author-only or partial results), so neither the LLM nor the template can name the others. (2) A junk
credit "Author (Illustrator)" on OL27482W became "Michael Hague, Author and Alan Lee"; role-word names are dropped, and
role labels parse by whole words ("also credited" had been read as "editor"). (3) "known in English as also published as
Bilbo le Hobbit": the edition title is a fact only when it is the title evidence of a tier A–C match, its label is
masked with it as one verbatim clause, and the check rejects any other label in front of it (`garbled_label`).

| Provider / model | Batch | Calls | Grounding pass | Latency | Tokens in / out |
|---|---|---|---|---|---|
| gemini / gemini-3.5-flash-lite | e2e problem items (6) | 1 | **6 / 6** | 3.31 s | 875 / 318 |
| gemini / gemini-3.5-flash-lite | edition titles, other scripts (4) | 1 | **4 / 4** | 3.90 s | 738 / 208 |
| anthropic / claude-haiku-5-5 | e2e problem items (6) | 1 | **6 / 6** | 2.86 s | 1,589 / 369 |

- gemini, OL20651930W: "Near title match; also published as Bilbo le Hobbit; Charles Dixon is the primary author; 2 editions."
- gemini, OL69630W (was: three narrator/illustrator credits): "Partial title match; Frances Hodgson Burnett is the primary author; 600 editions."
- anthropic, OL219602W: "Exact title match; J.R.R. Tolkien is the primary author; Charles Dixon is listed as adapter; Sean Deming is listed as adapter; matched term: hobbit; 10 editions."
- Tom Sawyer Abroad (author-only) no longer carries an edition title; OL27482W no longer shows the "Author" credit.

### Revision 3: prompt `explain.v4`, topic requests (2026-10-25)

`explain.v4` adds two lines to v3: quote `matched_subjects` exactly as given, and never say what a "Suggested for this
topic" item is about. Batch `find me a book about bears` (3 subject matches, 1 verified suggestion without subjects):

| Provider / model | Calls | Grounding pass | Latency | Tokens in / out |
|---|---|---|---|---|
| gemini / gemini-3.5-flash-lite | 1 | **4 / 4** | 1.66 s | 782 / 140 |
| anthropic / claude-haiku-5-5 | 1 | **4 / 4** | 1.58 s | 1,462 / 229 |

- both: "Topic match; A. A. Milne is the primary author; Open Library lists the subjects Bears and Teddy bears; 87 editions."
- both: "Suggested for this topic; Michael Bond is the primary author; 120 editions." (no topic claim)

After subjects became whole headings ("Bears -- Juvenile fiction", not just "Bears") and listed subjects had to be
subject facts, one more Gemini call on the same batch: **4 / 4**, headings quoted verbatim ("Open Library lists the
subjects Bears -- Juvenile fiction and Teddy bears"). The two earlier sentences that quoted only "Bears" now fall back
to the template, by design.

Explanation smoke total: 13 calls (9 Gemini, 4 Anthropic), 65 / 65 sentences grounded at the time of each run.

## Topic requests (interpret.v4)

2026-10-25. Same test with `FTB_SMOKE_SET=topic`; prompt `interpret.v4` (v3 unchanged + intent, subjects, suggestions).
Raw adapter output, before Core sanitizing. 10 calls: Gemini 6, Anthropic 2, OpenAI 2.

| Model | Query | Status, latency, tokens in/out | intent | subjects | suggestions (title / author) |
|---|---|---|---|---|---|
| gemini-3.5-flash-lite | find me a book about bears | ok, 1.65 s, 388/220 | topic | bears | Winnie-the-Pooh / A.A. Milne; The Bear / Raymond Briggs; Corduroy / Don Freeman; We're Going on a Bear Hunt / Michael Rosen; Paddington / Michael Bond |
| gemini-3.5-flash-lite | books like harry potter | ok, 1.97 s, 386/258 | topic | fantasy fiction, magic, boarding schools | The Lightning Thief; The Golden Compass; The Lion, the Witch and the Wardrobe; A Wizard of Earthsea; Nevermoor (title null) |
| gemini-3.5-flash-lite | un livre sur les ours | ok, 1.13 s, 387/173 | topic | bears | Winnie-the-Pooh; The Bear; Paddington Bear |
| gemini-3.5-flash-lite | much ado about nothing | ok, 0.92 s, 386/79 | **book** | — | — (title Much Ado About Nothing / William Shakespeare) |
| gemini-3.5-flash-lite | the hobbit | ok, 0.95 s, 385/142 | **book** | — | — (The Hobbit / J.R.R. Tolkien) |
| gemini-3.5-flash-lite | recommend me a good book | ok, 1.08 s, 387/118 | topic | books, reading, bestsellers | five classics (Gatsby, Mockingbird, 1984, P&P, Catcher) |
| claude-haiku-5-5 | find me a book about bears | **timeout** (4.0 s, first call of the process) | — | — | — |
| claude-haiku-5-5 | books like harry potter | ok, 2.23 s, 1153/217 | topic | wizards, fantasy fiction, schools | The Lightning Thief; A Wizard of Earthsea; The Magicians; Eragon; The Chronicles of Narnia |
| gpt-4.1-mini | find me a book about bears | ok, 2.32 s, 530/127 | topic | bears, animals, nature | The Bear (Krivak); The Jungle Book; Brown Bear, Brown Bear…; The Bear and the Nightingale; Gentle Giants… |
| gpt-4.1-mini | books like harry potter | ok, 1.50 s, 530/118 | topic | fantasy fiction, magic, young adult fiction | Narnia; Percy Jackson; His Dark Materials; The Hobbit; The Golden Compass |

Observations:
- Intent was right on all 9 answers: the two real titles containing topic-ish words stayed `book`, and the French
  request became English subject headings ("bears").
- Every model left title and author null on the topic requests, so the "typed or suggested" title rule had nothing to
  drop here; it stays as a guard.
- "recommend me a good book" produced generic headings (books, reading, bestsellers) and five classics. Core now drops
  generic headings and, with no subject left, the suggestions too, so the request ends as "add a topic, a title or an
  author" instead of the model's reading list.
- Cost of the bigger prompt: Gemini output grew from ~80 to 120–260 tokens on topic answers (p50 ~1.1 s across the six);
  Anthropic input went from ~740 to 1,153 tokens and its first call hit the 4 s interpret timeout; OpenAI input ~530.
  gpt-4.1-mini adds broad headings (animals, nature) next to the specific one.

### interpret.v5: topic only for requests (2026-10-25)

The golden Gemini run with v4 labelled bare author and character names and a one-book plot as topics ("dickens" →
subjects [charles dickens]; "hermione" → [fantasy fiction, magic, wizards]). v5 = v3 unchanged + intent "book" for a
bare author, character, series or title or one book's plot, "topic" only for requests for books in general, and four
contrast examples chosen to appear in no eval set (zadie smith, ignatius reilly, books about lighthouses, des livres sur
les chats). Core also refuses a topic label without a typed signal (the cleaner's topic frame or request phrase, a plural
or collective book noun, like/similar, recommend/suggest; for longer text also about/regarding/set in…) and drops
subjects that are just the author's name. 7 Gemini calls (`FTB_SMOKE_SET=intent`):

| Query | v5 raw answer | After Core | Latency, tokens in/out |
|---|---|---|---|
| dickens | book, author Charles Dickens | same | 1.09 s, 522/49 |
| hermione | book, Harry Potter and the Sorcerer's Stone / J.K. Rowling | same | 3.65 s, 523/77 |
| mark twain | book, author Mark Twain | same | 0.71 s, 523/69 |
| the purple elephant who sang opera on mars | book, title echoed (not in Open Library → none, AI-19) | same | 0.82 s, 528/111 |
| find me a book about bears | topic [bears]; Winnie-the-Pooh, The Bear, The Golden Compass | same | 1.06 s, 526/167 |
| books like harry potter | topic [fantasy fiction, magic, boarding schools]; five fantasy titles | same | 2.09 s, 524/169 |
| un livre sur les ours | topic [bears]; Winnie-the-Pooh, The Bear, The Great Bear | same | 1.02 s, 525/160 |

All 7 intents right with the prompt alone; the Core guards are the deterministic backstop (unit-tested on the five v4
mislabels and on the topic rows). The prompt is now ~520 input tokens on Gemini (v3: ~240).
