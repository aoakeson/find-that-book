# Compare: A = `2026-10-25-synthetic-mirror-500-none-default` → B = `2026-10-25-synthetic-mirror-500-none-baseline`

| | A | B |
|---|---|---|
| dataset | synthetic mirror queries v1 (4e28a8b5b31e3d2b) | synthetic mirror queries v1 (4e28a8b5b31e3d2b) |
| catalog | mirror | mirror |
| provider | none | none |
| model | n/a | n/a |
| observed_models | none | none |
| judge | false | false |
| code_fingerprint | sha256:a70a9a21182a7288 (149 files) | sha256:4edb729c53600731 (149 files) |
| started | 2026-10-25T02:10:05.7972500-06:00 | 2026-10-25T02:15:29.0762580-06:00 |

| Category | n A/B | top-1 A → B | top-5 A → B | status A → B | `match` @ P A → B | false match A → B |
|---|---|---|---|---|---|---|
| **overall** | 500/500 | .832 → .830 (-0.2) | .860 → .860 (=) | n/a → n/a | 273 @ .993 → 275 @ .982 | 0 → 0 |
| author_only | 89/89 | .787 → .787 (=) | .820 → .820 (=) | n/a → n/a | 2 @ .000 → 2 @ .000 | 0 → 0 |
| noisy | 69/69 | .986 → .986 (=) | .986 → .986 (=) | n/a → n/a | 58 @ 1.000 → 58 @ 1.000 | 0 → 0 |
| partial | 73/73 | .959 → .959 (=) | .973 → .973 (=) | n/a → n/a | 40 @ 1.000 → 40 @ 1.000 | 0 → 0 |
| reordered | 51/51 | .922 → .902 (-2.0) | .980 → .980 (=) | n/a → n/a | 40 @ 1.000 → 40 @ .975 | 0 → 0 |
| title_author | 85/85 | .976 → .976 (=) | .988 → .988 (=) | n/a → n/a | 67 @ 1.000 → 67 @ 1.000 | 0 → 0 |
| title_only | 66/66 | .879 → .879 (=) | .939 → .939 (=) | n/a → n/a | 50 @ 1.000 → 52 @ .962 | 0 → 0 |
| typo | 67/67 | .299 → .299 (=) | .328 → .328 (=) | n/a → n/a | 16 @ 1.000 → 16 @ 1.000 | 0 → 0 |

**Top-1 fixed (A miss → B hit): 0**

**Top-1 broken (A hit → B miss): 1**: `synthetic-mirror.v1-0140` pocket word search sudoku (match → ambiguous)

**Status fixed: 0**

**Status broken: 0**

**New wrong or false matches in B: 3**: `synthetic-mirror.v1-0124` financial management (ambiguous → match), `synthetic-mirror.v1-0273` vehicle maintenance log book (ambiguous → match), `synthetic-mirror.v1-0471` a set of six conrad (ambiguous → match)

Deltas are B − A in points (1 point = 0.01). On the 60-row golden set one query moves top-1 by about 1.8 points.
