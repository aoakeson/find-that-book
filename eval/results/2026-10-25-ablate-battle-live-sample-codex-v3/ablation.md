# Ranker ablation: battle live sample v1

> battle sample on LIVE Open Library (in-sample tuning data for the ranker-rule decision)

- Date: 2026-10-25 02:31 MT (-06:00) · 2026-10-25 04:31 ET (-04:00) · wall 2.4 min
- Code fingerprint: `sha256:b872851edf7c3974 (151 files)` · pipeline binaries `sha256:23d8df33d6e24992 (2 files)` · eval `sha256:1caf4d1d3f242957 (31 files)`
- Dataset: `eval/datasets/battle-live-sample.v1.jsonl` sha256:f2a1aacd619df805, 200 rows, tuning: in_sample: battle rounds the v3 fixes and the ranker's R3-xxxx rules were designed on; used here to decide which ranker rules stay (never the held-out set)
- Catalog: live behind the record-on-miss cache `eval/results/_catalog-cache/raw/live`
- Interpretations: replayed from `cache/hyp_v3.jsonl` · explanations: template · judge: off
- Command: `dotnet run --project tools/FindThatBook.Eval -- ablate --set eval/datasets/battle-live-sample.v1.jsonl --catalog live --catalog-cache eval/results/_catalog-cache/raw/live --hypotheses ../research/11-battle-test/cache/hyp_v3.jsonl --switches RequiredTitleWords,Mashup,HypothesisAuthorCheck,AuthoredExact,IntentRivals,SameTitleRival,TinyRecord --max-ol-requests 124 --concurrency 2 --label ablate-battle-live-sample-codex-v3`

Default arm: top-1 153/192 = .797, top-5 168/192 = .875, status 136/200 = .680, match 112/127 = .882, severe 0, errors 0; rows with an error or a failed catalog call in any arm (left out of breaks/fixes): 1.

Δ = arm − default, in points (1 point = 0.01); a negative Δ means the switched-off rule was helping. A row *passes* when top-1 and status are both right and it has no severe match; "off breaks" are rows that pass with the rule and fail without it, "off fixes" the reverse (rows run without error in both arms only). A rule can move one metric without flipping a whole row (for example turning a wrong match into an under-confident ambiguous), so read the severe column with the net.

| Arm (rule switched off) | top-1 Δ | top-5 Δ | status Δ | `match` n @ P (Δ P) | severe Δ | errors | off breaks | off fixes | net | verdict | broken by switching off (first 3) | fixed by switching off (first 3) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `-AuthoredExact` | = | = | -0.5 | 126 @ .889 (+0.7) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-HypothesisAuthorCheck` | +0.5 | = | = | 127 @ .882 (=) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-IntentRivals` | +0.5 | = | -1.0 | 125 @ .896 (+1.4) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-SameTitleRival` | = | = | = | 127 @ .882 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-TinyRecord` | = | = | = | 127 @ .882 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-Mashup` | = | = | +0.5 | 128 @ .883 (+0.1) | = | 0 | 0 | 1 | -1 | costs rows here |  | R3-0396 Blue: The History of a Color — … |
| `-RequiredTitleWords` | +2.6 | +1.6 | +2.0 | 131 @ .878 (-0.4) | +1 | 0 | 0 | 3 | -3 | trade-off (prevents 1 severe, costs 3 rows) |  | R3-0138 Can I get My Father Bleeds Hist…; R3-0344 watashi no shiawase na kekkon v…; R3-0392 SMASHED—Junji Ito Story Collect… |
| `baseline (every addition off)` | +2.6 | = | +1.5 | 130 @ .900 (+1.8) | +1 | 0 | 0 | 5 | -5 | trade-off (prevents 1 severe, costs 5 rows) |  | R1-0134 Find Living Nightmare by Darren…; R3-0138 Can I get My Father Bleeds Hist…; R3-0344 watashi no shiawase na kekkon v… |

Every broken / fixed row id per arm is in `ablation.json`; each arm's full results are under `raw/arms/` (gitignored). One row here = 0.50 points.
