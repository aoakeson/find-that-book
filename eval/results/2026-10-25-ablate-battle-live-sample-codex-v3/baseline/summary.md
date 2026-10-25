# Eval: battle live sample v1 · ablate-battle-live-sample-codex-v3 baseline (every addition off)

> battle sample on LIVE Open Library (in-sample tuning data for the ranker-rule decision)

| Run | |
|---|---|
| Date | 2026-10-25 02:30 MT (-06:00) · 2026-10-25 04:30 ET (-04:00) |
| Code fingerprint (src/) | `sha256:b872851edf7c3974 (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:23d8df33d6e24992 (2 files)` |
| Eval tool fingerprint | `sha256:1caf4d1d3f242957 (31 files)` |
| Dataset | battle live sample v1 (`eval/datasets/battle-live-sample.v1.jsonl`, sha256:f2a1aacd619df805) |
| Held out | no |
| Tuning | in_sample: battle rounds the v3 fixes and the ranker's R3-xxxx rules were designed on; used here to decide which ranker rules stay (never the held-out set) |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured); behind the record-on-miss cache `eval/results/_catalog-cache/raw/live` |
| Replay sources | `find-that-book-repo/eval/results/_catalog-cache/raw/live` (552) |
| Provider (requested) | none |
| Models (observed in traces) | interpret: replay/codex (research cache, prompt v3) |
| Interpretations | replayed from `research/11-battle-test/cache/hyp_v3.jsonl` (1356 entries; 0 hypothesis_miss rows ran LLM-off) |
| Explanations | template only (--explain template: the explain LLM is never called) |
| Ranker | `baseline` |
| Judge | off |
| Catalog cache | `eval/results/_catalog-cache/raw/live`: 0 requests fetched from the backend this run; 0 unsupported calls answered empty (person: on the mirror) |
| Caches | as configured (cold at process start) |
| Counts | 200 cases · 200 run · 0 skipped · 1 rejected by validation · 0 errors · 200 in-sample |
| Concurrency / limit | 2 / 5 |
| Wall time | 15.6 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- ablate --set eval/datasets/battle-live-sample.v1.jsonl --catalog live --catalog-cache eval/results/_catalog-cache/raw/live --hypotheses ../research/11-battle-test/cache/hyp_v3.jsonl --switches RequiredTitleWords,Mashup,HypothesisAuthorCheck,AuthoredExact,IntentRivals,SameTitleRival,TinyRecord --max-ol-requests 124 --concurrency 2 --label ablate-battle-live-sample-codex-v3` |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **117/130 = .900** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 158/192 = .823 | answerable rows, first shown candidate acceptable |
| Top-5 | 168/192 = .875 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 139/200 = .695 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 130/192 = .677 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 8 | any `match` where no book is right |
| Severe matches | 1 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 8 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| adversarial | 4 | 1/1 = 1.000 | 1/1 = 1.000 | 4/4 = 1.000 | 1 @ 1.000 | 0 |
| alt_title | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 3/4 = .750 | 3 @ 1.000 | 0 |
| ambiguous_title | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 0/4 = .000 | 4 @ .000 | 0 |
| audiobook_phrasing | 2 | 0/2 = .000 | 1/2 = .500 | 0/2 = .000 | 0 @ n/a | 0 |
| author_alias | 14 | 10/14 = .714 | 11/14 = .786 | 12/14 = .857 | 12 @ .750 | 0 |
| author_only | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 4/4 = 1.000 | 0 @ n/a | 0 |
| author_plus_clue | 3 | 3/3 = 1.000 | 3/3 = 1.000 | 0/3 = .000 | 0 @ n/a | 0 |
| author_wrong_title | 4 | 3/4 = .750 | 3/4 = .750 | 0/4 = .000 | 0 @ n/a | 0 |
| character | 6 | 6/6 = 1.000 | 6/6 = 1.000 | 6/6 = 1.000 | 6 @ 1.000 | 0 |
| chatty_filler | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 4/4 = 1.000 | 4 @ 1.000 | 0 |
| coauthor | 3 | 3/3 = 1.000 | 3/3 = 1.000 | 3/3 = 1.000 | 3 @ 1.000 | 0 |
| decorated_titles | 14 | 11/14 = .786 | 13/14 = .929 | 9/14 = .643 | 9 @ 1.000 | 0 |
| format_words | 4 | 3/4 = .750 | 4/4 = 1.000 | 3/4 = .750 | 3 @ 1.000 | 0 |
| gibberish | 4 | n/a | n/a | 3/4 = .750 | 0 @ n/a | 0 |
| jp_en_duplicates | 14 | 12/14 = .857 | 13/14 = .929 | 12/14 = .857 | 12 @ .917 | 0 |
| kid_vague | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 3/4 = .750 | 3 @ 1.000 | 0 |
| noisy_paste | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 4/4 = 1.000 | 3 @ 1.000 | 0 |
| nonfiction_edition | 3 | 0/2 = .000 | 1/2 = .500 | 2/3 = .667 | 1 @ .000 | 0 |
| numbers | 4 | 3/4 = .750 | 3/4 = .750 | 4/4 = 1.000 | 4 @ .750 | 0 |
| partial_title | 3 | 2/3 = .667 | 2/3 = .667 | 2/3 = .667 | 2 @ 1.000 | 0 |
| pen_name | 14 | 12/14 = .857 | 13/14 = .929 | 12/14 = .857 | 12 @ 1.000 | 0 |
| pen_name_obscure | 14 | 13/14 = .929 | 13/14 = .929 | 14/14 = 1.000 | 14 @ .929 | 0 |
| picture_books | 2 | 2/2 = 1.000 | 2/2 = 1.000 | 1/2 = .500 | 1 @ 1.000 | 0 |
| plot | 14 | 12/14 = .857 | 12/14 = .857 | 1/14 = .071 | 1 @ 1.000 | 0 |
| poetry_anthology | 2 | 2/2 = 1.000 | 2/2 = 1.000 | 2/2 = 1.000 | 1 @ 1.000 | 0 |
| quote | 4 | 3/4 = .750 | 3/4 = .750 | 0/4 = .000 | 0 @ n/a | 0 |
| recent_releases | 2 | 2/2 = 1.000 | 2/2 = 1.000 | 2/2 = 1.000 | 2 @ 1.000 | 0 |
| romanized_title | 3 | 2/3 = .667 | 2/3 = .667 | 2/3 = .667 | 2 @ .500 | 0 |
| series | 14 | 12/14 = .857 | 12/14 = .857 | 11/14 = .786 | 11 @ 1.000 | 0 |
| short_title | 4 | 2/4 = .500 | 4/4 = 1.000 | 2/4 = .500 | 2 @ 1.000 | 0 |
| stopword_title | 4 | 4/4 = 1.000 | 4/4 = 1.000 | 4/4 = 1.000 | 4 @ 1.000 | 0 |
| translation | 14 | 8/14 = .571 | 8/14 = .571 | 7/14 = .500 | 7 @ .857 | 0 |
| typos | 3 | 3/3 = 1.000 | 3/3 = 1.000 | 3/3 = 1.000 | 3 @ 1.000 | 0 |

## Replay

Requests answered from recordings: 569 (of which fetched from the backend and cached in this run: 0); unsupported (answered empty): 0; **replay_miss: 0 requests on 0 rows** (a miss returned an empty result; those rows' scores are not trustworthy).
replay_partial (a recording shorter than the limit asked, or without person): 0 requests on 0 rows.

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 0, template 847 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 184

## Degraded

- Degraded rate: 184/199 = .925
  - of which 183 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/199 = .000
- explanation_fallback: 184
- catalog_partial: 1
- Interpretation source: llm 199

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 199 | 4 ms | 17 ms | 15018 ms |
| Pipeline elapsed_ms | 199 | 4 ms | 17 ms | 15018 ms |
| stage `clean` | 199 | 0 ms | 0 ms | 0 ms |
| stage `explain` | 184 | 0 ms | 1 ms | 23 ms |
| stage `interpret` | 199 | 0 ms | 0 ms | 0 ms |
| stage `judge` | 184 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 199 | 3 ms | 15 ms | 46 ms |
| stage `retrieve` | 199 | 4 ms | 15 ms | 15018 ms |
| Open Library call (not cache hits) | 570 | 0 ms | 0 ms | 15003 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 0 | n/a | n/a | n/a |

## Open Library calls

- Logical calls per query: mean 2.86, max 5, at budget 3 (budget: 5 logical calls)
- Cache hits: 0/570 = .000
- HTTP attempts: 2 total, mean 0.01 per query; slow-empty re-queries 0
- Outcomes: ok 569, failed 1
- Steps: broad 199, hypothesis 185, original_title 81, any_of 74, person 17, person_any 6, author_works 4, person_fallback 2, person_fallback_any 2

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 184 | 0 | not_configured 184 | n/a | n/a |
| interpret | 199 | 199 | ok 199 | n/a | n/a |

- Paid LLM calls (not cache hits, incl. judge): 0
- Estimated cost: n/a (no priced calls)

## Misses (70)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| R1-0024 | Title fragment: sailor fell from grace | match · 午後の曳航 / 三島由紀夫 | none (weak_evidence) |  |
| R1-0061 | guy sneaks into alien landing zones selling weird junk, daughter gets furry, ends up hunting a golden wish ball | match · Roadside Picnic / Аркадий Стругацкий, Борис Стругацкий, Arkady and Boris Strugatsky | ambiguous (unsupported_guess), target at #1 | OL7967812W Roadside Picnic [A]; OL18396W The Marvelous Land of Oz [E]; OL81630W The Dead Zone [E] |
| R1-0063 | This man buys peasants who aren't alive anymore but still count on tax lists, so he can pretend he's rich. | match · Dead Souls / Nikolaĭ Vasilʹevich  Gogolʹ | ambiguous (unsupported_guess), target at #1 | OL17396611W Dead Souls [A]; OL699230W Мертвые души [A]; OL36116409W Dead Souls [A] |
| R1-0070 | maths teacher helps nextdoor lady hide her ex husbands killing, then his physicist friend tries cracking their alibi | match · The Devotion of Suspect X / Keigo Higashino, David Pittu | ambiguous (unsupported_guess), target at #1 | OL24633849W The Devotion of Suspect X [A]; OL34588369W The Devotion of Suspect X [A]; OL25154493W Devotion of Suspect X A Ind Loc [C] |
| R1-0071 | Newlyweds are found slain in a locked Japanese annex; a koto is heard, and a sword lies outside in undisturbed snow. | match · 本陣殺人事件 / Seishi Yokomizo | ambiguous (unsupported_guess), target at #1 | OL24716016W 本陣殺人事件 [A]; OL20939618W The Honjin Murders [A]; OL43758522W 本陣殺人事件 [A] |
| R1-0072 | On an island things disappear and everyone forgets them, but a lady hides her editor under her floor because he still remembers. | match · 密やかな結晶 / 小川洋子, Stephen Snyder - undifferentiated | ambiguous (unsupported_guess), target at #1 | OL11690127W 密やかな結晶 [A]; OL45560574W The Memory Police [A]; OL8090838W The Conspirators Hierarchy [E] |
| R1-0084 | What book says 'in a hole in the ground there lived a rabbit'? I think I got the animal wrong. | match · The Hobbit / J.R.R. Tolkien | ambiguous (unsupported_guess), target at #1 | OL27482W The Hobbit [A]; OL14926019W The Hobbit & The Lord of the Rings [col… [C]; OL14933471W Poems from The hobbit [C] |
| R1-0088 | It is far far better thing I do than I ever done — I remember ending, not beginning, which novel? | match · A Tale of Two Cities / Charles Dickens | ambiguous (unsupported_guess), target at #1 | OL8193465W A Tale of Two Cities [A]; OL14869944W A Tale of Two Cities with Connections [A]; OL14869333W Novels (A Tale of Two Cities / Great Ex… [A] |
| R1-0128 | I search abandoned baobab from Mariètou Mbaye, she use another writer name. | match · Le baobab fou / Ken Bugul | ambiguous (weak_evidence), target at #2 | OL17453W She [E]; OL2567069W Le baobab fou [A]; OL46241W I, Robot [E] |
| R1-0143 | Locate An-sky's דער דיבוק, also called Between Two Worlds; standalone play, English translation. | match · The Dybbuk / S. Ansky | ambiguous (close_scores), target at #1 | OL16233926W The Dybbuk [A]; OL41577343W Dybbuk [A]; OL8445352W DYBBUK AND OTHER WRITINGS, THE [E] |
| R1-0148 | The Golem, translated from Der Golem, Gustav Meyrink; English prose novel, not a folklore compilation. | match · Der Golem / Gustav Meyrink | ambiguous (close_scores), target at #1 | OL3416329W Der Golem [A]; OL3416298W The Golem [A]; OL35700794W Golem [A] |
| R1-0186 | I search To Himself of Marcus Aurelius, English translation please | match · Meditations / Marcus Aurelius | ambiguous (unsupported_guess), target at #1 | OL1317211W Meditations [A]; OL26667983W Meditations [A]; OL379949W The Apology, Phaedo and Crito of Plato;… [D] |
| R1-0279 | koji suzuki the grudge?? the book with the videotape that kills u after 7 days | match · The Ring / Kōji Suzuki | ambiguous (unsupported_guess) | OL16070670W Ring [A]; OL26294496W Ring [A]; OL22450309W RINGS [C] |
| R1-0282 | I want Mary Shelley's Dracula, where the man makes a living person from dead body parts. | match · Frankenstein; or, The Modern Prometheus / Mary Shelley | ambiguous (unsupported_guess), target at #1 | OL450063W Frankenstein; or, The Modern Prometheus [A]; OL25595002W Mary Shelley's Frankenstein; or, the Mo… [A]; OL26600526W Frankenstein Annotated [C] |
| R1-0311 | Devotion suspect X kindle edition English please, Higashino writer | match · The Devotion of Suspect X / Keigo Higashino, David Pittu | ambiguous (close_scores), target at #2 | OL15637392W The devotion of suspect X [A]; OL24633849W The Devotion of Suspect X [A]; OL29311657W The Devotion of the Suspect X [A] |
| R1-0361 | 🫠 | none · no such book | rejected (validation_failed) |  |
| R1-0383 | Title search: The Awakening; author unspecified. | ambiguous · The Awakening / Kate Chopin | match (exact_title_primary_author), target at #1 | OL65430W The Awakening [A]; OL65438W Works (Awakening / Beyond the Bayou / D… [D]; OL40451575W Awakening and Other Short Stories [D] |
| R1-0391 | Title search: Fallen; author unspecified. | ambiguous · Fallen / Lauren Kate | match (exact_title_primary_author), target at #1 | OL15299819W Fallen [A]; OL22822745W Fallen in Love [C]; OL20417780W Fallen 03. Passion [C] |
| R1-0418 | hey book bot find hidey by johanna spiry, mountain grandpa goat girl with a friend in a wheelchair, bet you cant | match · Heidi / Spyri, Johanna | ambiguous (unsupported_guess), target at #1 | OL28815175W Heidi [A]; OL26461430W Heidi [A]; OL21002674W Heidi [A] |
| R2-0057 | A sad woman tries different versions of herself by opening books, and her old school helper is there between being alive and dead. | match · The Midnight Library / Matt Haig | ambiguous (unsupported_guess), target at #1 | OL20965973W The Midnight Library [A]; OL17597290W American Film [E]; OL26557354W The Comfort Book / Reasons To Stay Aliv… [D] |
| R2-0064 | guy in paris gets a wish granting bit of leather but it shrnks every time and his life gets shorter too | match · La peau de chagrin / Honoré de Balzac | none (weak_evidence) |  |
| R2-0087 | Song of the Lioness 2. Yes TWO, genius, not another Tortall series. | match · In the Hand of the Goddess / Tamora Pierce | none (weak_evidence) |  |
| R2-0102 | Little House, volume VII in the original nine-book sequence, counting Farmer Boy. | match · Little Town on the Prairie / Laura Ingalls Wilder | none (weak_evidence) |  |
| R2-0106 | Dark Tower 2, genius. The BOOK after Gunslinger, not a movie. | match · The Drawing of the Three / Stephen King | ambiguous (unsupported_guess), target at #1 | OL81616W The Drawing of the Three [A]; OL81628W The Gunslinger [D]; OL81615W Wizard and Glass [D] |
| R2-0132 | عمارة يعقوبيان by علاء الأسواني, I need English words version for reading group | match · The Yacoubian Building / Alaa Al Aswany | ambiguous (author_only) | OL19784298W Advanced English Grammar [D]; OL46241W I, Robot [E]; OL30310548W My English Vocabulary [D] |
| R2-0147 | Find गबन by प्रेमचंद, clever bot. The Stolen Jewels, if your search needs training wheels. | match · Gaban / Munshi Premchand | none (weak_evidence) |  |
| R2-0149 | I want Три толстяка, the story with the girl Suok and a doll. | match · Three fat men / Yuri Olesha | ambiguous (weak_evidence) | OL77786W The story girl [E]; OL16249343W A Doll's House [E]; OL46241W I, Robot [E] |
| R2-0154 | Can I get 魔女の宅急便 by 角野栄子? The first story book about Kiki, not the movie comic. | match · Kiki's Delivery Service / Eiko Kadono, Daniel Kondo | none (weak_evidence) |  |
| R2-0157 | Le avventure di Cipollino, Rodari. Yes, the onion kid. Try finding a BOOK instead of giving me recipes. | match · Adventures of Cipollino / Gianni Rodari, Dasha Tolstikova, Antony Shugaar | match (exact_title_primary_author) | OL24314993W Le Avventure DI Cipollino [A]; OL29257919W Le Avventure DI Cipollino /Չիպոլինոյի ա… [C]; OL18203055W Le avventure Di Cipollino / Пригоди Циб… [C] |
| R2-0215 | Why? by Mario Livio, the book about being curious | match · Why? / Mario Livio | ambiguous (author_only), target at #2 | OL1673205W The Godfather [D]; OL19710763W Why? [A]; OL362691W Much Ado About Nothing [E] |
| R2-0259 | I want José Saramago book Enemy, about teacher who sees actor with his same face. | match · O Homem Duplicado / José Saramago | ambiguous (unsupported_guess), target at #1 | OL7923009W O Homem Duplicado [A]; OL27331930W Double [A]; OL25116936W Double : (Enemy) [A] |
| R2-0309 | I want the book called Gone | ambiguous · Gone / Michael Grant, Michael Grant | match (exact_title_primary_author), target at #1 | OL12514150W Gone [A]; OL16525015W Gone Volume 4 Plague [C]; OL20098483W Gone. Michael Grant [C] |
| R2-0366 | Tezuka Osamu Buddha, first book Kapilavastu, in English I need. | match · Budda / 手塚 治虫 | match (exact_title_primary_author) | OL5769514W Buddha, Volume 1 [A]; OL42922481W ゴータマ・ブッダ [E]; OL43049881W ブッダ・チャリタ [E] |
| R2-0371 | Kong Fu-tzu, Analects; complete text, any translation. | match · Lun yu / Confucius | match (exact_title_primary_author) | OL27033818W The Analects [A]; OL15335448W Confucian analects [C]; OL39989804W Analects of Confucius [C] |
| R2-0372 | I look for confession of Count Lev Tolstoi, his losing faith book. | match · Ispovedʹ / Лев Толстой | ambiguous (near_title_author), target at #1 | OL457792W My confession [C]; OL38223816W Confession [A]; OL36288171W Confession [B] |
| R2-0373 | Mary Flannery O'Connor: Wise Blood, standalone novel. | match · Wise blood / Flannery O'Connor | ambiguous (author_only), target at #2 | OL450063W Frankenstein; or, The Modern Prometheus [D]; OL65295W Wise blood [A]; OL151798W Goodnight Moon [D] |
| R2-0391 | Bet you can't find Tang Ji He De. Yes, the Cervantes novel, genius. | match · Don Quijote de la Mancha / Miguel de Cervantes Saavedra | match (exact_title_primary_author) | OL44306061W El ingenioso hidalgo Don Quijote de la … [A]; OL35162909W Ingenioso hidalgo Don Quijote de la Man… [A] |
| R2-0417 | which dumas book has the jailed guy get rich and ruin everyone who framed him, petty king lol | match · Le Comte de Monte Cristo [2/2] / Alexandre Dumas | ambiguous (unsupported_guess), target at #1 | OL36287W Le Comte de Monte-Cristo [A]; OL20201553W The Count of Monte Cristo [A]; OL15196749W Le Comte De Monte Cristo 1 [A] |
| R2-0421 | mary oliver poetry handbook the one that teaches you poems in normal sentences | match · A Poetry Handbook / Mary Oliver | ambiguous (author_only), target at #2 | OL450063W Frankenstein; or, The Modern Prometheus [D]; OL13534159W A Poetry Handbook [A]; OL8193478W Oliver Twist [E] |
| R3-0054 | bug collectr misses last bus, villagers lower him into sandy pit then remove ladder; has to shovel every night with a widow | match · 砂の女 / Abe Kōbō | ambiguous (unsupported_guess), target at #1 | OL2644757W 砂の女 [A]; OL31567789W The woman in the dunes [A]; OL43573244W The woman in the dunes [A] |
| R3-0055 | bloke hiding on island falls for tourist who ignores him, turns out whole group is a recording repeating forever. creepy screensaver romance lol | match · La invención de Morel / Adolfo Bioy Casares | ambiguous (unsupported_guess), target at #1 | OL14944491W La invención de Morel [A]; OL14944456W The Invention of Morel [A]; OL45575102W La invención de morel / Plan de evasio… [C] |
| R3-0057 | youngest daughter cannot marry because must care mother, her sweetheart marry sister instead and feelings enter food she cooks | match · Como agua para chocolate / Laura Esquivel | ambiguous (unsupported_guess), target at #1 | OL953162W Como agua para chocolate [A]; OL37478208W Like Water for Chocolate [A]; OL44583168W Mi Negro Pasado (Como Agua para Chocola… [D] |
| R3-0060 | Find the novel about a Chinese silk-factory worker financing family crises by selling plasma, including treatment for a son he knows isn't biologically his. | match · Xu Sanguan mai xue ji / 余华 | none (weak_evidence) |  |
| R3-0063 | a kitty tells about riding around Japan in a silver van while his person asks old friends to adopt him, but we learn his person is really sick | match · The Travelling Cat Chronicles / 有川ひろ | ambiguous (unsupported_guess), target at #1 | OL19551795W The Travelling Cat Chronicles [A]; OL44306537W The travelling cat chronicles [A]; OL44560912W Travelling Cat Chronicles and the Goodb… [C] |
| R3-0064 | rich guy dies and 16 ppl get paired up with money and weird clues to figure out who killed him, theres a girl who kicks shins | match · The Westing Game / Ellen Raskin | ambiguous (unsupported_guess), target at #1 | OL4103857W The Westing Game [A]; OL3792044W The science of getting rich, or, financ… [D]; OL527464W Think and Grow Rich [E] |
| R3-0067 | you pierce my soul im half agony half hope — which austen?? | match · Persuasion / Jane Austen | ambiguous (unsupported_guess), target at #1 | OL66544W Persuasion [A]; OL66512W Novels (Emma / Mansfield Park / Northan… [C]; OL29250W In the Hand of the Goddess [D] |
| R3-0071 | Locate the novel containing 'Truth becomes fiction when the fiction’s true'; translations may vary. | match · Hong lou meng / Tsʻao, Hsüeh-chʻin., (qing) Cao, xue qin, Cao xue qin | ambiguous (unsupported_guess) | OL22427641W The Dream of The Red Chamber [A]; OL44635074W 紅樓夢 [A]; OL42888892W 紅樓夢 [A] |
| R3-0096 | Mayombe, by Artur Carlos Maurício Pestana dos Santos; English translation. | match · Mayombe / Pepetela. | ambiguous (author_only) | OL278437W La sombra del viento [D]; OL872225W Aura [D]; OL19784298W Advanced English Grammar [D] |
| R3-0107 | Identify 占星術殺人事件 by 島田荘司; English translation, standalone novel. | match · Tokyo Zodiac Murders / 島田荘司, Ross Mackenzie, Shika Mackenzie | none (weak_evidence) |  |
| R3-0147 | I seek Musashi book of 5 rings, sword strategy teaching | match · Gorin no sho / Miyamoto Musashi | match (exact_title_primary_author) | OL39568417W The Book of Five Rings [A]; OL37791428W A Book of Five Rings [A]; OL38366815W Book of Five Rings and Dokkodo [C] |
| R3-0163 | gulp mary roch | match · Gulp / Mary Roach | ambiguous (author_only), target at #4 | OL450063W Frankenstein; or, The Modern Prometheus [D]; OL450124W The Last Man [D]; OL849186W A Vindication of Rights of Woman [D] |
| R3-0198 | achebe long walk to freedom? young guy comes back frm england to a nigeria govt job | match · No Longer at Ease / Chinua Achebe | ambiguous (unsupported_guess), target at #1 | OL891789W No Longer at Ease [A]; OL891786W Things Fall Apart [D]; OL891787W A Man of the People [D] |
| R3-0253 | Seeking The Other, with no author information available. | ambiguous · The Other / TRYON | match (exact_title_primary_author), target at #1 | OL34622773W The Other [A]; OL5225533W A new art of brewing beer, ale, and oth… [D]; OL1136093W ... A monograph of the order Pholadacea… [D] |
| R3-0283 | By Miyazaki Hayao, I want valley wind manga volume 1 please. | match · Nausicaa of the Valley of the Wind, Vol. 1 / Hayao Miyazaki | match (exact_title_primary_author) | OL28758703W 風の谷のナウシカ [A]; OL3963421W 風の谷のナウシカ 6 [C]; OL3963430W 風の谷のナウシカ 7 [C] |
| R3-0288 | Find sonyeoni onda by Han Kang, genius. Bet your search chokes on Korean without Hangul. | match · 소년이 온다 / Han Kang, Alba Cunill Fulquet | ambiguous (unsupported_guess), target at #1 | OL17764836W 소년이 온다 [A]; OL34809632W Actes humans [C]; OL16176918W Confronting the Human Rights Act 1998 [D] |
| R3-0297 | tuchmn history one abt plague and knights in the 1300s | match · A Distant Mirror / Barbara Tuchman | ambiguous (unsupported_guess), target at #1 | OL3378547W A Distant Mirror [A]; OL15394545W Distant Mirror-V1 [C]; OL15394546W Distant Mirror-V2 [C] |
| R3-0301 | Drucker: the management book organized around five practices, beginning with knowing where one's time goes. | match · Effective Executive / Peter F. Drucker, Joseph A. Maciariello, Jim Collins | ambiguous (unsupported_guess), target at #1 | OL272920W Effective Executive [A]; OL40642374W Effective Executive [A]; OL43372163W The Effective Executive  [A] |
| R3-0315 | Confucius Lun yu book please, English Analects with D C Lau translating | match · Lun yu / Confucius | match (exact_title_primary_author) | OL27033818W The Analects [A]; OL15335448W Confucian analects [C]; OL39989804W Analects of Confucius [C] |
| R3-0320 | kikis delivry service kadono original novel book 1 | match · Kiki's Delivery Service / Eiko Kadono, Daniel Kondo | ambiguous (close_scores), target at #1 | OL20760219W Kiki's Delivery Service [A]; OL26649W Kiki's Delivery Service [A]; OL25071263W 魔女の宅急便 [A] |
| R3-0327 | Brave Story Miyabe I want whole prose novel of Wataru not comic volumes | match · Bureibu sutōrī / Miyuki Miyabe, Yoichiro Ono | match (exact_title_primary_author) | OL15596245W Brave story [A]; OL17742695W ブレイブ・ストーリー (3) [A]; OL33797944W Brave Story 3 [C] |
| R3-0343 | I want the pancreas book by Yoru Sumino, the story with words not the comic | match · 君の膵臓をたべたい (light novel) / 住野よる | ambiguous (close_scores), target at #2 | OL42530098W I Want to Eat Your Pancreas (manga) [A]; OL20010923W 君の膵臓をたべたい (light novel) [A]; OL46241W I, Robot [E] |
| R3-0362 | I want Dodgson's Looking Glass book where Alice goes through the mirror | match · Through the Looking-Glass / Lewis Carroll | match (exact_title_contributor_author) | OL29579120W Through the Looking-Glass, and What Ali… [B]; OL43369583W Through the looking-glass, and what Ali… [A]; OL40179442W Through the Looking Glass (and What Ali… [D] |
| R3-0381 | hunger: memoir of (my) body roxan gay | match · Hunger / Roxane Gay | ambiguous (weak_evidence) | OL573193W Sult [E]; OL5735363W The Hunger Games [E]; OL5735360W Catching Fire [E] |
| R3-0391 | I want SHIVER ☆ selected stories by Junji Ito, the scary comic book. | match · Shiver / Junji Itō | ambiguous (weak_evidence), target at #2 | OL11967025W Shiver [E]; OL19735101W Shiver [A]; OL34966583W Junji Ito [D] |
| R3-0397 | I need BLACK - history of a color by Michel Pastoureau for my colors project. | match · Black / Michel Pastoureau | ambiguous (close_scores), target at #1 | OL36147715W Black [A]; OL625814W Noir [A]; OL28587908W Pierre n'a plus peur du noir [D] |
| R3-0400 | ghos ts ~a graphic novel~ raina telgemeir pls | match · Ghosts / Raina Telgemeier | ambiguous (unsupported_guess), target at #1 | OL17603398W Ghosts [A]; OL20659167W ghosts [A]; OL16793507W Drama [D] |
| R3-0401 | I want SISTERS (a graphic novel) by Raina, the Smile lady. | match · Sisters / Raina Telgemeier | ambiguous (close_scores), target at #2 | OL37989771W Sisters: A graphic novel [A]; OL17202765W Sisters [A]; OL25228569W Smile, Sisters, and Guts [C] |
| R3-0409 | Seeking Anno's original wordless European travel picture book, Japanese title Tabi no Ehon, rather than the later Britain volume. | match · Anno's Journey / Mitsumasa Anno | ambiguous (unsupported_guess), target at #1 | OL4297777W Anno's Journey [A]; OL9066265W Annos Journey III :JAPANESE [C]; OL9066266W Annos Journey II :JAPANESE [C] |
| R3-0472 | Need audiobook work record: Fitzharris, Joseph Lister and Victorian surgery, title sounds like 'butcher's art'; not a surgical manual. | match · The butchering art / Lindsey Fitzharris | ambiguous (author_only), target at #4 | OL38663W Heart of Darkness [D]; OL39108W The Secret Agent [D]; OL55806W The Power of Your Subconscious Mind [D] |
| R3-0479 | Identify the underlying work: unabridged CD, Bryan Cranston reading linked Vietnam stories featuring Jimmy Cross and Kiowa. | match · The Things They Carried / Tim O'Brien | ambiguous (author_only) | OL167331W The Alibi [D]; OL17231441W Just Mercy [D]; OL5859250W Scott Pilgrim's Precious Little Life [D] |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
