# Ranker ablation: battle rounds r1-r3 v1

> synthetic adversarial, local mirror (no editions.title/alt names/person:), relative comparisons only

- Measured: 2026-10-25 02:06 MT (-06:00) · 2026-10-25 04:06 ET (-04:00) (default arm start; table re-rendered 2026-10-25 02:16 MT (-06:00) · 2026-10-25 04:16 ET (-04:00))
- Code fingerprint: `sha256:62f0fdef8259815f (149 files)` · pipeline binaries `sha256:23d8df33d6e24992 (2 files)` · eval `sha256:093cc1f310aaaee0 (31 files)`
- Dataset: `../research/11-battle-test/rounds` sha256:83fca5c42c766ebe, 1356 rows, tuning: in_sample: research/11 battle rounds; plan 06 v3 fixes and the ranker's R3-xxxx rules were designed on them
- Catalog: mirror behind the record-on-miss cache `eval/results/_catalog-cache/raw/mirror`
- Interpretations: none (LLM off) · explanations: template · judge: off
- Command: `dotnet run --project tools/FindThatBook.Eval -- ablate --set battle --catalog mirror --provider none --concurrency 6 --label ablate-battle-r1-r3-llm-off --config OpenLibrary:AttemptTimeout=00:01:00 --config OpenLibrary:TotalTimeout=00:02:00 --config OpenLibrary:SlowEmptyThreshold=00:00:55 --config OpenLibrary:BreakerSamplingDuration=00:02:00 --config OpenLibrary:BreakerFailureRatio=1 --config OpenLibrary:BreakerMinimumThroughput=100000 --config OpenLibrary:MaxRetries=1 --config Search:RequestDeadline=00:04:00`

Default arm: top-1 468/1284 = .364, top-5 553/1284 = .431, status 143/1356 = .105, match 16/20 = .800, severe 1, errors 0.

Δ = arm − default, in points (1 point = 0.01); a negative Δ means the switched-off rule was helping. A row *passes* when top-1 and status are both right and it has no severe match; "off breaks" are rows that pass with the rule and fail without it, "off fixes" the reverse (rows run without error in both arms only). A rule can move one metric without flipping a whole row (for example turning a wrong match into an under-confident ambiguous), so read the severe column with the net.

| Arm (rule switched off) | top-1 Δ | top-5 Δ | status Δ | `match` n @ P (Δ P) | severe Δ | errors | off breaks | off fixes | net | verdict | broken by switching off (first 3) | fixed by switching off (first 3) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `-AuthoredExact` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-CharacterWord` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-ExactOverPartial` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-HypothesisAuthorCheck` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-IntentRivals` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-LoneSurname` | = | = | +0.1 | 21 @ .762 (-3.8) | +1 | 0 | 0 | 0 | = | earns its keep (prevents 1 severe) |  |  |
| `-Mashup` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-NamedContributor` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-NoneGuard` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-PerformerRoles` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-RequiredTitleWords` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-SameTitleRival` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-SeriesName` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-SoftTokens` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-SubtitleWorks` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `-TinyRecord` | = | = | +0.1 | 21 @ .762 (-3.8) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-TypedAuthorTiny` | +0.1 | +0.1 | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | mixed (no whole-row change) |  |  |
| `-VolumeMarkers` | = | = | = | 20 @ .800 (=) | = | 0 | 0 | 0 | = | no effect here |  |  |
| `baseline (every addition off)` | +0.2 | +0.2 | +0.1 | 21 @ .762 (-3.8) | +1 | 0 | 0 | 0 | = | earns its keep (prevents 1 severe) |  |  |

Every broken / fixed row id per arm is in `ablation.json`; each arm's full results are under `raw/arms/` (gitignored). One row here = 0.07 points.
