# Ranker ablation: battle rounds r1-r3 v1

> synthetic adversarial, local mirror (no editions.title/alt names/person:), relative comparisons only

- Measured: 2026-10-25 02:03 MT (-06:00) · 2026-10-25 04:03 ET (-04:00) (default arm start; table re-rendered 2026-10-25 02:16 MT (-06:00) · 2026-10-25 04:16 ET (-04:00))
- Code fingerprint: `sha256:ff59c652fb4d5247 (149 files)` · pipeline binaries `sha256:23d8df33d6e24992 (2 files)` · eval `sha256:093cc1f310aaaee0 (31 files)`
- Dataset: `../research/11-battle-test/rounds` sha256:83fca5c42c766ebe, 1356 rows, tuning: in_sample: research/11 battle rounds; plan 06 v3 fixes and the ranker's R3-xxxx rules were designed on them
- Catalog: mirror behind the record-on-miss cache `eval/results/_catalog-cache/raw/mirror`
- Interpretations: replayed from `cache/hyp_v3.jsonl` · explanations: template · judge: off
- Command: `dotnet run --project tools/FindThatBook.Eval -- ablate --set battle --catalog mirror --hypotheses ../research/11-battle-test/cache/hyp_v3.jsonl --concurrency 6 --label ablate-battle-r1-r3-codex-v3 --config OpenLibrary:AttemptTimeout=00:01:00 --config OpenLibrary:TotalTimeout=00:02:00 --config OpenLibrary:SlowEmptyThreshold=00:00:55 --config OpenLibrary:BreakerSamplingDuration=00:02:00 --config OpenLibrary:BreakerFailureRatio=1 --config OpenLibrary:BreakerMinimumThroughput=100000 --config OpenLibrary:MaxRetries=1 --config Search:RequestDeadline=00:04:00`

Default arm: top-1 929/1284 = .724, top-5 1088/1284 = .847, status 800/1356 = .590, match 630/732 = .861, severe 6, errors 0.

Δ = arm − default, in points (1 point = 0.01); a negative Δ means the switched-off rule was helping. A row *passes* when top-1 and status are both right and it has no severe match; "off breaks" are rows that pass with the rule and fail without it, "off fixes" the reverse (rows run without error in both arms only). A rule can move one metric without flipping a whole row (for example turning a wrong match into an under-confident ambiguous), so read the severe column with the net.

| Arm (rule switched off) | top-1 Δ | top-5 Δ | status Δ | `match` n @ P (Δ P) | severe Δ | errors | off breaks | off fixes | net | verdict | broken by switching off (first 3) | fixed by switching off (first 3) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `-ExactOverPartial` | -0.1 | = | -0.1 | 731 @ .860 (=) | = | 0 | 1 | 0 | +1 | earns its keep | R3-0106 I want デスノート 1, the first book … |  |
| `-CharacterWord` | = | = | = | 733 @ .861 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-LoneSurname` | = | = | = | 732 @ .861 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-NamedContributor` | +0.4 | = | -0.1 | 731 @ .862 (+0.1) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-NoneGuard` | = | -0.1 | -0.1 | 732 @ .861 (=) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-PerformerRoles` | = | = | = | 732 @ .861 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-SeriesName` | = | = | = | 732 @ .861 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-SoftTokens` | = | = | = | 732 @ .861 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-SubtitleWorks` | = | = | = | 732 @ .861 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-TinyRecord` | = | = | +0.7 | 741 @ .850 (-1.0) | +5 | 0 | 0 | 0 | = | earns its keep (prevents 5 severe) |  |  |
| `-TypedAuthorTiny` | +0.3 | = | = | 732 @ .861 (=) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-VolumeMarkers` | +0.1 | -0.1 | = | 732 @ .861 (=) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-AuthoredExact` | +0.5 | +0.2 | -0.1 | 731 @ .862 (+0.1) | = | 0 | 0 | 1 | -1 | costs rows here |  | R1-0290 tolstoy madam bovary pls, marri… |
| `-IntentRivals` | +0.1 | = | -0.1 | 730 @ .864 (+0.4) | = | 0 | 0 | 1 | -1 | costs rows here |  | R2-0287 Please original verse novel The… |
| `-SameTitleRival` | = | = | +0.1 | 735 @ .859 (-0.2) | = | 0 | 0 | 1 | -1 | costs rows here |  | R2-0031 Kamala Govinda |
| `-HypothesisAuthorCheck` | +1.1 | +0.5 | -0.2 | 727 @ .875 (+1.4) | = | 0 | 6 | 13 | -7 | costs rows here | R2-0139 I want 容疑者Xの献身 in English, the …; R2-0230 where she went is name I think,…; R2-0392 can i get guri to gura where th… | R1-0021 kafka by the beach; R1-0024 Title fragment: sailor fell fro…; R1-0065 An oral account of the 1986 rea… |
| `-Mashup` | = | = | +0.8 | 745 @ .862 (+0.1) | +1 | 0 | 0 | 12 | -12 | trade-off (prevents 1 severe, costs 12 rows) |  | R1-0104 Asimov: Foundation original tri…; R1-0137 Samuel Langhorne Clemens, The A…; R1-0327 wow can your genius box read th… |
| `-RequiredTitleWords` | +0.2 | = | +1.3 | 750 @ .865 (+0.5) | -1 | 0 | 1 | 20 | -19 | costs rows here and 1 severe | R3-0143 can i get 1 punch man book one … | R1-0173 the Dan Heath book about why so…; R1-0194 Locate Jared Diamond's A Short …; R1-0195 Richard Dawkins: Why the Eviden… |
| `baseline (every addition off)` | +3.7 | +0.5 | +1.8 | 763 @ .885 (+2.4) | = | 0 | 1 | 47 | -46 | costs rows here | R3-0143 can i get 1 punch man book one … | R1-0021 kafka by the beach; R1-0024 Title fragment: sailor fell fro…; R1-0065 An oral account of the 1986 rea… |

Every broken / fixed row id per arm is in `ablation.json`; each arm's full results are under `raw/arms/` (gitignored). One row here = 0.07 points.
