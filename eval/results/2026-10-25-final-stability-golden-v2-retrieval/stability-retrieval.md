# Retrieval variability: Find That Book golden set v2

- Date: 2026-10-25 01:54 MT (-06:00) · 2026-10-25 03:54 ET (-04:00)
- Code fingerprint: `sha256:0acf0a6c4a1e5263 (146 files)` · pipeline binaries `sha256:23d8df33d6e24992 (2 files)` · eval `sha256:2f47929f61d18649 (31 files)`
- Dataset: `eval/golden.v2.json` sha256:5b70cc75033b87c6, 20 cases
- Provider: none · catalog: live · judge: off · caches: bypassed · runs: 3
- Command: `stability --mode retrieval --runs 3 --limit 20 --label final-stability-golden-v2-retrieval`

Queries: 20 × 3 runs; errors: 0; skipped (budget): 0; catalog cache hits observed: 0 (must be 0 for a real measurement).

Over the 20 queries where every run completed:

| Stability across all runs (share of queries) | Value |
|---|---|
| **Top-1 identical** | **20/20 = 1.000** |
| Top-5 set identical | 20/20 = 1.000 |
| Top-5 order identical | 20/20 = 1.000 |
| Status identical | 20/20 = 1.000 |
| Ranked-pool Jaccard (mean of per-query mean pairwise) | 1.000 (min 1.000) |

Pool = the full ranked list after dedupe (SearchOutcome.Ranking), compared as sets of work ids.
