# Compare: A = `default` → B = `baseline`

| | A | B |
|---|---|---|
| dataset | battle live sample v1 (f2a1aacd619df805) | battle live sample v1 (f2a1aacd619df805) |
| catalog | live | live |
| provider | none | none |
| model | n/a | n/a |
| observed_models | interpret: replay/codex (research cache, prompt v3) | interpret: replay/codex (research cache, prompt v3) |
| judge | false | false |
| code_fingerprint | sha256:b872851edf7c3974 (151 files) | sha256:b872851edf7c3974 (151 files) |
| started | 2026-10-25T02:28:41.1983110-06:00 | 2026-10-25T02:30:48.0893780-06:00 |

| Category | n A/B | top-1 A → B | top-5 A → B | status A → B | `match` @ P A → B | false match A → B |
|---|---|---|---|---|---|---|
| **overall** | 200/200 | .797 → .823 (+2.6) | .875 → .875 (=) | .680 → .695 (+1.5) | 127 @ .882 → 130 @ .900 | 0 → 0 |
| adversarial | 4/4 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1 @ 1.000 → 1 @ 1.000 | 0 → 0 |
| alt_title | 4/4 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | .500 → .750 (+25.0) | 2 @ 1.000 → 3 @ 1.000 | 0 → 0 |
| ambiguous_title | 4/4 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | .000 → .000 (=) | 4 @ .000 → 4 @ .000 | 0 → 0 |
| audiobook_phrasing | 2/2 | .000 → .000 (=) | .000 → .500 (+50.0) | .000 → .000 (=) | 0 @ n/a → 0 @ n/a | 0 → 0 |
| author_alias | 14/14 | .786 → .714 (-7.1) | .857 → .786 (-7.1) | .786 → .857 (+7.1) | 11 @ .818 → 12 @ .750 | 0 → 0 |
| author_only | 4/4 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 0 @ n/a → 0 @ n/a | 0 → 0 |
| author_plus_clue | 3/3 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | .000 → .000 (=) | 0 @ n/a → 0 @ n/a | 0 → 0 |
| author_wrong_title | 4/4 | .750 → .750 (=) | .750 → .750 (=) | .000 → .000 (=) | 0 @ n/a → 0 @ n/a | 0 → 0 |
| character | 6/6 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 6 @ 1.000 → 6 @ 1.000 | 0 → 0 |
| chatty_filler | 4/4 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 4 @ 1.000 → 4 @ 1.000 | 0 → 0 |
| coauthor | 3/3 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 3 @ 1.000 → 3 @ 1.000 | 0 → 0 |
| decorated_titles | 14/14 | .643 → .786 (+14.3) | .929 → .929 (=) | .571 → .643 (+7.1) | 8 @ .875 → 9 @ 1.000 | 0 → 0 |
| format_words | 4/4 | .750 → .750 (=) | 1.000 → 1.000 (=) | .750 → .750 (=) | 3 @ 1.000 → 3 @ 1.000 | 0 → 0 |
| gibberish | 4/4 | n/a → n/a | n/a → n/a | .750 → .750 (=) | 0 @ n/a → 0 @ n/a | 0 → 0 |
| jp_en_duplicates | 14/14 | .786 → .857 (+7.1) | .857 → .929 (+7.1) | .857 → .857 (=) | 12 @ .833 → 12 @ .917 | 0 → 0 |
| kid_vague | 4/4 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | .750 → .750 (=) | 3 @ 1.000 → 3 @ 1.000 | 0 → 0 |
| noisy_paste | 4/4 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 3 @ 1.000 → 3 @ 1.000 | 0 → 0 |
| nonfiction_edition | 3/3 | .000 → .000 (=) | .500 → .500 (=) | .667 → .667 (=) | 1 @ .000 → 1 @ .000 | 0 → 0 |
| numbers | 4/4 | .750 → .750 (=) | .750 → .750 (=) | 1.000 → 1.000 (=) | 4 @ .750 → 4 @ .750 | 0 → 0 |
| partial_title | 3/3 | .667 → .667 (=) | 1.000 → .667 (-33.3) | .667 → .667 (=) | 2 @ 1.000 → 2 @ 1.000 | 0 → 0 |
| pen_name | 14/14 | .786 → .857 (+7.1) | .929 → .929 (=) | .786 → .857 (+7.1) | 11 @ 1.000 → 12 @ 1.000 | 0 → 0 |
| pen_name_obscure | 14/14 | .929 → .929 (=) | .929 → .929 (=) | 1.000 → 1.000 (=) | 14 @ .929 → 14 @ .929 | 0 → 0 |
| picture_books | 2/2 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | .500 → .500 (=) | 1 @ 1.000 → 1 @ 1.000 | 0 → 0 |
| plot | 14/14 | .786 → .857 (+7.1) | .857 → .857 (=) | .071 → .071 (=) | 1 @ 1.000 → 1 @ 1.000 | 0 → 0 |
| poetry_anthology | 2/2 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1 @ 1.000 → 1 @ 1.000 | 0 → 0 |
| quote | 4/4 | .750 → .750 (=) | .750 → .750 (=) | .000 → .000 (=) | 0 @ n/a → 0 @ n/a | 0 → 0 |
| recent_releases | 2/2 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 2 @ 1.000 → 2 @ 1.000 | 0 → 0 |
| romanized_title | 3/3 | .667 → .667 (=) | .667 → .667 (=) | .667 → .667 (=) | 2 @ .500 → 2 @ .500 | 0 → 0 |
| series | 14/14 | .857 → .857 (=) | .929 → .857 (-7.1) | .786 → .786 (=) | 11 @ 1.000 → 11 @ 1.000 | 0 → 0 |
| short_title | 4/4 | .500 → .500 (=) | 1.000 → 1.000 (=) | .500 → .500 (=) | 2 @ 1.000 → 2 @ 1.000 | 0 → 0 |
| stopword_title | 4/4 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 4 @ 1.000 → 4 @ 1.000 | 0 → 0 |
| translation | 14/14 | .500 → .571 (+7.1) | .500 → .571 (+7.1) | .571 → .500 (-7.1) | 8 @ .750 → 7 @ .857 | 0 → 0 |
| typos | 3/3 | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 1.000 → 1.000 (=) | 3 @ 1.000 → 3 @ 1.000 | 0 → 0 |

**Top-1 fixed (A miss → B hit): 6**: `R1-0070` maths teacher helps nextdoor lady hide her ex husbands killing, then his physicist friend tries cracking their alibi (ambiguous → ambiguous), `R1-0134` Find Living Nightmare by Darren O'Shaughnessy, genius—original novel, not the manga. (ambiguous → match), `R1-0143` Locate An-sky's דער דיבוק, also called Between Two Worlds; standalone play, English translation. (none → ambiguous), `R3-0344` watashi no shiawase na kekkon vol 1 LIGHT NOVEL, don't give me the damn manga (none → match), `R3-0392` SMASHED—Junji Ito Story Collection, genius; don't give me somebody's drinking memoir. (ambiguous → match), `R3-0397` I need BLACK - history of a color by Michel Pastoureau for my colors project. (ambiguous → ambiguous)

**Top-1 broken (A hit → B miss): 1**: `R3-0283` By Miyazaki Hayao, I want valley wind manga volume 1 please. (ambiguous → match)

**Status fixed: 6**: `R1-0134` Find Living Nightmare by Darren O'Shaughnessy, genius—original novel, not the manga. (ambiguous → match), `R3-0138` Can I get My Father Bleeds History by Spiegelman? The first mouse comic book. (ambiguous → match), `R3-0283` By Miyazaki Hayao, I want valley wind manga volume 1 please. (ambiguous → match), `R3-0344` watashi no shiawase na kekkon vol 1 LIGHT NOVEL, don't give me the damn manga (none → match), `R3-0392` SMASHED—Junji Ito Story Collection, genius; don't give me somebody's drinking memoir. (ambiguous → match), `R3-0396` Blue: The History of a Color — Michel Pastoureau; translated from Bleu, histoire d'une couleur. (ambiguous → match)

**Status broken: 3**: `R3-0107` Identify 占星術殺人事件 by 島田荘司; English translation, standalone novel. (match → none), `R3-0343` I want the pancreas book by Yoru Sumino, the story with words not the comic (match → ambiguous), `R3-0401` I want SISTERS (a graphic novel) by Raina, the Smile lady. (match → ambiguous)

**New wrong or false matches in B: 1**: `R3-0283` By Miyazaki Hayao, I want valley wind manga volume 1 please. (ambiguous → match)

Deltas are B − A in points (1 point = 0.01). On the 60-row golden set one query moves top-1 by about 1.8 points.
