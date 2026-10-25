# Interpretation stability: Find That Book golden set v2

- Date: 2026-10-25 01:45 MT (-06:00) · 2026-10-25 03:45 ET (-04:00)
- Code fingerprint: `sha256:dd58f98dffa8042f (145 files)` · eval `sha256:84e1f7d2bb43601b (31 files)`
- Dataset: `eval/golden.v2.json` sha256:5b70cc75033b87c6, 20 cases
- Provider: gemini · catalog: live · judge: off · caches: bypassed · runs: 3
- Command: `stability --mode interpret --provider gemini --runs 3 --limit 20 --max-llm-calls 60 --label final-stability-golden-v2-interpret-gemini`

Queries: 20 (validated, non-empty after cleaning) × 3 runs = 60 interpreter calls; cache hits observed: 0 (must be 0 for a real measurement); non-ok calls: 0; runs without a hypothesis: 0.

| Agreement across all runs (share of queries) | Value |
|---|---|
| **Normalized title + author identical** | **18/20 = .900** |
| title | 18/20 = .900 |
| author | 20/20 = 1.000 |
| english_title | 20/20 = 1.000 |
| original_title | 20/20 = 1.000 |
| characters (set) | 20/20 = 1.000 |
| keywords (set) | 12/20 = .600 |
| Whole hypothesis identical | 12/20 = .600 |
| Source identical (llm vs heuristic) | 20/20 = 1.000 |

| Unstable query | Distinct title / author answers |
|---|---|
| moby dick | moby dick / herman melville · moby dick or the whale / herman melville |
| 1984 orwell | 1984 / george orwell · nineteen eighty four / george orwell |

Normalization: the pipeline's TextFolding.Tokens (folded, lower-case, punctuation dropped); characters/keywords compared as sets; a failed call counts as the value ∅.
