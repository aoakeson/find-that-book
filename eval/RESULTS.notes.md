### Pending runs
- **Gemini, after its daily quota resets.** The Gemini free tier allows 15 requests/min and 500 requests/day per model
  (both measured from 429 quota details: `GenerateRequestsPerMinutePerProjectPerModel-FreeTier = 15`,
  `GenerateRequestsPerDayPerProjectPerModel-FreeTier = 500`). The daily quota ran out on 2026-10-25 during the topic
  run, so the held-out, hard and topic LLM arms were run with OpenAI `gpt-4.1-mini` and Anthropic `claude-haiku-5-5`.
  Still to run with Gemini:
  - **topic.v2:** re-run the 13 rows the quota spoiled (`run --set topic --catalog live --provider gemini --resume
    eval/results/2026-10-25-final3-topic-v2-live-gemini`). The other 21 topic rows each got a real interpretation:
    18/21 on topic.
  - **hard:** `run --set hard --catalog live --provider gemini --explain template --hypotheses
    eval/results/2026-10-25-final3-golden-v2-live-gemini-v5/results.jsonl --hypotheses-fill live` (≈ 16 new calls; the
    other 30 hard queries are golden queries).
  - **interpretation stability on interpret.v5** (3 × 20, ≈ 60 calls). The figure shown (18/20 identical title + author)
    was measured on the interpret.v3 build.

### How to read the numbers
- **In-sample vs held-out.** Golden, hard, topic and the battle rounds were used while the rules were built and tuned,
  so they measure regressions and fidelity to the design. The held-out set (409 rows) was sealed, run once on the
  final build, and nothing was changed because of it.
- **Arms and providers.** Each row names the model that produced its interpretations. "interpret" arms ran with
  `--explain template`, so their explanation numbers are not LLM explanations. "interpretations replayed" arms reuse
  the interpretations a named earlier run recorded (`--hypotheses`); their summaries state the build that recorded them
  and the build that ranked them.
- **Explanations** were measured on golden with the explain LLM on (Gemini 3.5 flash-lite): 287 LLM sentences,
  2 template sentences after grounding rejections, 0 fallbacks. Explanations depend on the matched facts, not on the
  interpret prompt, so that run stands for the final build.
- **Provider errors are not model quality.** Rows where a provider timed out, failed or rate-limited (including the
  cooldown after a 429) are counted separately; runs stop above 20% of such rows and `--resume` re-runs only them.
- **Battle rounds** are adversarial in-sample queries. The mirror runs carry the label "synthetic adversarial, local
  mirror (no editions.title/alt names/person:), relative comparisons only"; the live ablation used a 200-row stratified
  sample on live Open Library so the mirror's missing fields could not bias it.

### Findings recorded by the eval
- **Golden replay fixtures (2026-10-25)** were re-recorded live with the v3 fields (`editions.title`,
  `author_alternative_name`). The offline replay equals the live LLM-off run: top-1 56/57, top-5 56/57, status 56/60,
  match 41/41. Floors moved once, top-5 and status 57 → 56, because of character-04 "holden caulfield" (LLM off): on the
  live data a former wrong match became an honest `none`. The only evidence for *The Catcher in the Rye* is an Italian
  edition title; with an LLM on, the interpreter supplies the title.
- **The interpret.v4 regression** was caught by the golden Gemini run. The topic-aware prompt sent author-only queries
  (dickens, orwell, mark twain) and a character name (hermione) down the topic path, and gave a nonsense title topical
  suggestions. interpret.v5 plus sanitizer guards fixed it: golden with v5 matches the v3 run row for row (top-1,
  status and reason on all 60 rows).
- **Ranker rules.** On the mirror the baseline (every addition after the v3 prototype switched off) looked 46 rows
  better than the default. On live Open Library the difference is 5 rows (−5 net, +1 severe). The mirror gap came from
  the alias and edition-title data the mirror lacks. RequiredTitleWords is the one real live trade-off (3 rows against
  one wrong match on "valley wind manga volume 1"). TinyRecord prevents 5 severe matches on the mirror.

### Build provenance
- **Final build:** source `918d7b88`, pipeline binaries `bb794a4f` (interpret.v5, topic search). Every summary stamps
  both the source fingerprint read at start and the hash of the pipeline binaries it loaded. Earlier builds appear only
  under "All runs".
- **Earlier builds.** `dd58f98d` / `23d8df33`: the pre-topic build, used by the battle ablations and the earlier
  provider comparison. `ebda00bc` / `81564537`: interpret.v4. Runs whose source tree changed later while the same
  binaries were used are matched by their binary hash.
- **Reading the two fingerprints.** The source fingerprint covers every file under `src/`, API documentation included
  (for example the OpenAPI example files), so it also moves when only those change; the pipeline-binaries hash is the
  one that tracks ranking behaviour. The dlls embed their build path, so a checkout built in another folder prints a
  different binary hash for the same source.
