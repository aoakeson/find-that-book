# Eval: synthetic mirror queries v1 · synthetic-mirror-500-none-default

> **Synthetic, local mirror (bm25, OR fallback), not Open Library's ranking.** The mirror ignores `author_key` and `sort`, has no person index (`person:` calls are answered empty and counted) and no `editions.title` / `author_alternative_name`, so the numbers measure the cleaner, the retrieval ladder and the ranker on machine-generated noise, not headline quality.

| Run | |
|---|---|
| Date | 2026-10-25 02:10 MT (-06:00) · 2026-10-25 04:10 ET (-04:00) |
| Code fingerprint (src/) | `sha256:a70a9a21182a7288 (149 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:23d8df33d6e24992 (2 files)` |
| Eval tool fingerprint | `sha256:6408f042696f29ac (31 files)` |
| Dataset | synthetic mirror queries v1 (`eval/datasets/synthetic-mirror.v1.jsonl`, sha256:4e28a8b5b31e3d2b) |
| Held out | no |
| Tuning | never used for tuning (generated for this build); still machine noise over well-known works, so it measures the cleaner and the retrieval ladder, not headline quality |
| Catalog | MIRROR http://127.0.0.1:8090/: synthetic, local mirror (bm25, OR fallback), not Open Library's ranking; behind the record-on-miss cache |
| Replay sources | `find-that-book-repo/eval/results/_catalog-cache/raw/mirror` (3603) |
| Provider (requested) | none |
| Models (observed in traces) | none (no LLM call reached a provider) |
| Interpretations | none (LLM off) |
| Explanations | template (LLM off) |
| Ranker | `default` |
| Judge | off |
| Catalog cache | `eval/results/_catalog-cache/raw/mirror`: 736 requests fetched from the backend this run; 99 unsupported calls answered empty (person: on the mirror) |
| Caches | as configured (cold at process start) |
| Counts | 500 cases · 500 run · 0 skipped · 0 rejected by validation · 0 errors · 0 in-sample |
| Concurrency / limit | 6 / 5 |
| Wall time | 321.6 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set synthetic --catalog mirror --limit 500 --provider none --ranker default --concurrency 6 --quiet --label synthetic-mirror-500-none-default --config OpenLibrary:AttemptTimeout=00:01:00 --config OpenLibrary:TotalTimeout=00:02:00 --config OpenLibrary:SlowEmptyThreshold=00:00:55 --config OpenLibrary:BreakerSamplingDuration=00:02:00 --config OpenLibrary:BreakerFailureRatio=1 --config OpenLibrary:BreakerMinimumThroughput=100000 --config OpenLibrary:MaxRetries=1 --config Search:RequestDeadline=00:04:00` |
| Per-row results | `raw/results.jsonl` (500 rows; gitignored, kept locally) |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **271/273 = .993** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 416/500 = .832 | answerable rows, first shown candidate acceptable |
| Top-5 | 430/500 = .860 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | n/a | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 273/500 = .546 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 0 | any `match` where no book is right |
| Severe matches | 2 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 0 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| author_only | 89 | 70/89 = .787 | 73/89 = .820 | n/a | 2 @ .000 | 0 |
| noisy | 69 | 68/69 = .986 | 68/69 = .986 | n/a | 58 @ 1.000 | 0 |
| partial | 73 | 70/73 = .959 | 71/73 = .973 | n/a | 40 @ 1.000 | 0 |
| reordered | 51 | 47/51 = .922 | 50/51 = .980 | n/a | 40 @ 1.000 | 0 |
| title_author | 85 | 83/85 = .976 | 84/85 = .988 | n/a | 67 @ 1.000 | 0 |
| title_only | 66 | 58/66 = .879 | 62/66 = .939 | n/a | 50 @ 1.000 | 0 |
| typo | 67 | 20/67 = .299 | 22/67 = .328 | n/a | 16 @ 1.000 | 0 |

## Replay

Requests answered from recordings: 748 (of which fetched from the backend and cached in this run: 736); unsupported (answered empty): 99; **replay_miss: 0 requests on 0 rows** (a miss returned an empty result; those rows' scores are not trustworthy).
replay_partial (a recording shorter than the limit asked, or without person): 0 requests on 0 rows.

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 0, template 2269 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 480

## Degraded

- Degraded rate: 500/500 = 1.000
  - of which 500 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/500 = .000
- ai_not_configured: 500
- explanation_fallback: 480
- Interpretation source: heuristic 500

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 500 | 619 ms | 13846 ms | 67004 ms |
| Pipeline elapsed_ms | 500 | 619 ms | 13846 ms | 67004 ms |
| stage `clean` | 500 | 0 ms | 0 ms | 2 ms |
| stage `explain` | 480 | 0 ms | 0 ms | 10 ms |
| stage `interpret` | 500 | 0 ms | 0 ms | 8 ms |
| stage `judge` | 480 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 500 | 2 ms | 10 ms | 27 ms |
| stage `retrieve` | 500 | 619 ms | 13846 ms | 67004 ms |
| Open Library call (not cache hits) | 847 | 136 ms | 11006 ms | 37189 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 0 | n/a | n/a | n/a |

## Open Library calls

- Logical calls per query: mean 1.69, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 0/847 = .000
- HTTP attempts: 736 total, mean 1.47 per query; slow-empty re-queries 0
- Outcomes: ok 847
- Steps: broad 500, any_of 159, author_works 89, person_fallback 55, person_fallback_any 44

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 480 | 0 | not_configured 480 | n/a | n/a |
| interpret | 500 | 0 | not_configured 500 | n/a | n/a |

- Paid LLM calls (not cache hits, incl. judge): 0
- Estimated cost: n/a (no priced calls)

## Misses (84)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| synthetic-mirror.v1-0010 | james |  · author: Henry James | ambiguous (author_only) | OL132483W The Writings of James Monroe [D]; OL29565728W Coyote [D]; OL38801882W The poetical works of James Thomson [D] |
| synthetic-mirror.v1-0018 | group |  · author: Scottish Primary Mathematics Group | ambiguous (weak_evidence) | OL29198625W Process Groups [E]; OL21242855W Group of Seven [E]; OL28483906W Group Mind [E] |
| synthetic-mirror.v1-0059 | dept |  · author: Boston (Mass ) Election Dept | ambiguous (author_only) | OL28537439W Treasury Bulletin [D]; OL19775328W Annual Report Of The Commissioner Of Th… [D]; OL12071135W BIENNIAL REPORT OF THE METROPOLITAN TOR… [D] |
| synthetic-mirror.v1-0062 | masch vens in furs |  · Venus in Furs / Leopold von Sacher-Masoch | ambiguous (author_only) | OL43578411W Gesetze, Verordnungen und Verfügungen, … [D]; OL43688451W Gesetze, verordnungen und verfügungen w… [D]; OL100435W Maschere nude [E] |
| synthetic-mirror.v1-0064 | thorau a week on the cocnord and merrmack rviers |  · A week on the Concord and Merrimack Rivers / Henry David Thoreau | ambiguous (author_only) | OL15435309W Klang – Struktur – Metapher [D]; OL4506927W The Lion of Egypt [D]; OL20812342W Die Nebel von Winram [D] |
| synthetic-mirror.v1-0076 | united states |  · author: United States | ambiguous (author_only) | OL28537782W United States Census of Agriculture : 1… [D]; OL13106480W Professional Paper - United States Geol… [D]; OL16912797W Bulletin - United States National Museum [D] |
| synthetic-mirror.v1-0093 | flower publishing |  · author: Flower Publishing | ambiguous (author_only) | OL21892701W Merry Christmas [D]; OL30150740W Mandala Adult Coloring Book Black Backg… [D]; OL30149590W Mandala Coloring Book for Kids [D] |
| synthetic-mirror.v1-0094 | bensun michael |  · Michael / E. F. Benson | ambiguous (author_only) | OL2319985W Digging for gold, or, Adventures in Cal… [D]; OL25437631W Anything But Ordinary [D]; OL5448132W Michael Ayrton [D] |
| synthetic-mirror.v1-0095 | in journal to write in |  · Journal to Write In / Journal to Journal to Write In | ambiguous (author_only), target at #5 | OL40184197W Write Here Journals [D]; OL38154498W Beautiful Perfect Bound Journal to Writ… [E]; OL40226245W Travel Journal [E] |
| synthetic-mirror.v1-0098 | man |  · author: M. R. N. An.Unforgettable.Man | match (exact_title) | OL96241W Man to Man [A]; OL10463884W My many-coated man [E]; OL11121181W What manner of man [E] |
| synthetic-mirror.v1-0124 | financial management |  · Financial management / United States. General Accounting Office | ambiguous (close_scores) | OL20443856W Financial Management [A]; OL55013W Financial management [A]; OL20333150W Financial Management [A] |
| synthetic-mirror.v1-0125 | taylor |  · author: Mildred D. Taylor | ambiguous (author_only) | OL26537000W Taylor Swift [D]; OL22223487W Whole Works of the Right Rev. Jeremy Ta… [D]; OL1147283W The  dramatic works of Bayard Taylor [D] |
| synthetic-mirror.v1-0127 | zone flowurs cloring book |  · Flowers Coloring Book / Colors And Zone | none (weak_evidence) |  |
| synthetic-mirror.v1-0130 | wolpole jedith parsi |  · Judith Paris / Hugh Walpole | none (weak_evidence) |  |
| synthetic-mirror.v1-0132 | guestboks baby showre gest book |  · Baby Shower Guest Book / Sassy Guestbooks | ambiguous (author_only) | OL30458140W C'est Ma Première Fête ! Livre d'or - B… [D]; OL25353049W Baby Shower Guest Book [E]; OL278433W A Life Less Ordinary [D] |
| synthetic-mirror.v1-0136 | smih the theoyr of moal setniments |  · The theory of moral sentiments / Adam Smith | ambiguous (author_only) | OL4235300W Jean Le Moal [D]; OL38261439W Superfection Design 2019 Planner - Hard… [D]; OL40255207W BedNote [D] |
| synthetic-mirror.v1-0151 | aesop s fables aesop |  · Aesop's Fables / Aesop | ambiguous (author_only), target at #2 | OL15464922W Fables [D]; OL12129731W Aesop's Fables [D]; OL15112445W Fables from Aesop [D] |
| synthetic-mirror.v1-0156 | huiiinga eramus |  · Erasmus / Johan Huizinga | ambiguous (weak_evidence) | OL29643241W Eramus [E]; OL42546195W Poppy Makes Pockets [D]; OL13287186W A practical framework for community lia… [D] |
| synthetic-mirror.v1-0157 | gescheekideen terminolaner |  · Terminplaner / Fitness Kalen Bodybuilder Geschenkideen | none (weak_evidence) |  |
| synthetic-mirror.v1-0162 | haggaod the lady of blssholme |  · The Lady of Blossholme / H. Rider Haggard | none (weak_evidence) |  |
| synthetic-mirror.v1-0166 | chsenutt the colonl s draem |  · The Colonel's Dream / Charles Waddell Chesnutt | none (weak_evidence) |  |
| synthetic-mirror.v1-0177 | wales |  · author: England and Wales | ambiguous (weak_evidence) | OL14903289W Government of Wales Bill [E]; OL4576601W The Description of Wales [E]; OL22222034W Beauties of England and Wales [E] |
| synthetic-mirror.v1-0180 | moris a drem of john ball a king s lesson |  · A Dream of John Ball / A king's Lesson / William Morris | ambiguous (author_only), target at #2 | OL278688W A Dream of John Ball [D]; OL47747W A Dream of John Ball / A king's Lesson [C]; OL28698652W Dream of John Ball; and, a King's Lesson [C] |
| synthetic-mirror.v1-0182 | ainiworth the lancashie witches a romnace of pnedle forets |  · The Lancashire Witches, a Romance of Pendle Forest / William Harrison Ainsworth | ambiguous (weak_evidence) | OL21288672W Witches [E]; OL25802893W Witches [E]; OL9425710W The Witches [E] |
| synthetic-mirror.v1-0192 | baiely my wrold |  · My World / Donna Bailey | none (weak_evidence) |  |
| synthetic-mirror.v1-0199 | bume otherwie kown as seila the graet |  · Otherwise Known as Sheila the Great / Judy Blume | ambiguous (author_only) | OL21313812W Nanopatterning and Nanoscale Devices fo… [D]; OL24376404W History of the Jews [E]; OL20344672W Alchemy ? The Mysteries of the  Philoso… [D] |
| synthetic-mirror.v1-0208 | 40house of lords official report mcnally |  · 40house of Lords Official Report / Rand McNally | ambiguous (near_title_author), target at #2 | OL14903445W House of Lords Official Report (Parliam… [C]; OL14903292W 40house of Lords Official Report [A]; OL14903332W 40house of Commons Official Report (Par… [C] |
| synthetic-mirror.v1-0214 | arnodl essys |  · Essays / Matthew Arnold | none (weak_evidence) |  |
| synthetic-mirror.v1-0219 | cristie poirto invstigates |  · Poirot investigates / Agatha Christie | ambiguous (author_only) | OL20203162W Innovation and the State [D]; OL21813902W Ferret [D]; OL22311123W Bluetooth [D] |
| synthetic-mirror.v1-0243 | asimvo the end of eterity |  · The End of Eternity / Isaac Asimov | none (weak_evidence) |  |
| synthetic-mirror.v1-0246 | simpon diabetos tauebuch |  · Diabetes Tagebuch / Carl Simpson | ambiguous (author_only) | OL37729168W Anatomy and Physiology [D]; OL11014588W Preliminary list of the Simpson Cycles … [D]; OL19775753W Tiny Paul the Tarantula Takes a Walk [D] |
| synthetic-mirror.v1-0251 | baum |  · author: L. Frank Baum | ambiguous (author_only) | OL26010764W Workbook [D]; OL14861982W Un de Baumugnes [E]; OL1409561W Willi Baumeister [E] |
| synthetic-mirror.v1-0255 | she and allan |  · She and Allan / H. Rider Haggard | ambiguous (author_only), target at #2 | OL85864W Dracula's guest, and other weird stories [D]; OL17532W She and Allan [E]; OL17453W She [E] |
| synthetic-mirror.v1-0256 | lewis |  · author: C. S. Lewis | ambiguous (author_only) | OL24406921W The Life and Correspondence of M.G. Lew… [D]; OL1245713W Lewis Carroll [D]; OL1349255W One-way song [D] |
| synthetic-mirror.v1-0257 | languand piesr plowmn |  · Piers Plowman / William Langland | none (weak_evidence) |  |
| synthetic-mirror.v1-0258 | sudoku pocket sudoku |  · Pocket Sudoku Easy / Simply Sudoku | ambiguous (near_title_author) | OL21894772W Pocket Sudoku Easy [C]; OL21894774W Pocket Sudoku Hard [C]; OL21887027W Pocket Sudoku Hard [C] |
| synthetic-mirror.v1-0264 | scitt the betrohed |  · The Betrothed / Sir Walter Scott | none (weak_evidence) |  |
| synthetic-mirror.v1-0273 | vehicle maintenance log book |  · Vehicle Maintenance Log Book / Krizia Krizia Capeles | ambiguous (close_scores), target at #5 | OL30938618W Vehicle Maintenance Log Book [A]; OL21890354W Vehicle Maintenance Log Book [C]; OL27851716W Vehicle Maintenance Log Book [C] |
| synthetic-mirror.v1-0276 | wirght the csaino mureer case |  · The Casino Murder Case / Willard Huntington Wright | ambiguous (author_only) | OL31891738W The Pasta Bible [D]; OL29823044W Paliser Case [E]; OL11604702W Piet Heyn en de zilvervloot, bescheiden… [E] |
| synthetic-mirror.v1-0277 | maisse matiose |  · Matisse / Henri Matisse | ambiguous (author_only) | OL6669113W De Maisse [D]; OL12598808W A journal of all that was accomplished … [D]; OL13287048W A journal of all that was accomplished … [D] |
| synthetic-mirror.v1-0279 | shakspeare remeo and juloet |  · Romeo and Juliet / William Shakespeare | ambiguous (author_only) | OL258636W The Beauties of Shakspeare [D]; OL21474187W Shakspeare's Dramatische Werke [D]; OL24465192W The Plays and Poems of William Shakspea… [D] |
| synthetic-mirror.v1-0283 | contant adoophe |  · Adolphe / Benjamin Constant | ambiguous (author_only) | OL11132686W Histoire des différens peuples du monde [D]; OL6420395W Community development corporations [D]; OL24167056W Enchantement [D] |
| synthetic-mirror.v1-0286 | corwnell pont of origni |  · Point of Origin / Patricia Cornwell | ambiguous (author_only) | OL28834598W Fifth Origin [D]; OL26093795W Assassinat du Pont Rouge [E]; OL149802W Mithridates King of Pontus [E] |
| synthetic-mirror.v1-0303 | greeen staboul trian |  · Stamboul train / Graham Greene | ambiguous (author_only) | OL28157123W Indoor Marijuana Gardening [D]; OL41706561W Wolfs [D]; OL45024860W Tiger Jungle [D] |
| synthetic-mirror.v1-0309 | james |  · author: Montague Rhodes James | ambiguous (author_only) | OL132483W The Writings of James Monroe [D]; OL29565728W Coyote [D]; OL38801882W The poetical works of James Thomson [D] |
| synthetic-mirror.v1-0310 | poe |  · author: Edgar Allan Poe | match (exact_title), target at #2 | OL33935W Poe Poe Poe Poe Poe Poe Poe [A]; OL24181185W Les poèmes d'Edgar Poe [D]; OL26301481W Poems [D] |
| synthetic-mirror.v1-0322 | 2022 2022 vintage unabridged 2021 |  · 2022 / Leseevo USA 2022 | ambiguous (close_scores) | OL27249069W 2021-2022 [A]; OL27248647W 2021-2022 [A]; OL25520854W Agenda Escolar 2021 2022 [E] |
| synthetic-mirror.v1-0327 | horowitz |  · author: Anthony Horowitz | ambiguous (author_only) | OL20459236W America [D]; OL2781906W Crystallizing public opinion [D]; OL1576665W Shene luḥot ha-berit [D] |
| synthetic-mirror.v1-0329 | duuas le comet de motne crsto ii |  · Le Comte de Monte-Cristo, II / Alexandre Dumas | none (weak_evidence) |  |
| synthetic-mirror.v1-0330 | lodon taels of the fish patol |  · Tales of the Fish Patrol / Jack London | ambiguous (author_only) | OL16925355W The locus of tragedy [D]; OL13163007W Søren Kierkegaard als filosoof [D]; OL10611158W Hundrede taels [E] |
| synthetic-mirror.v1-0336 | post |  · author: Laurens van der Post | ambiguous (weak_evidence) | OL8287103W Saturday Evening Post [E]; OL6282369W Postcodes [D]; OL20423755W Emily Post's Etiquette [D] |
| synthetic-mirror.v1-0338 | doyee the adventuers of grard |  · The Adventures of Gerard / Arthur Conan Doyle | ambiguous (author_only) | OL21071142W Photon and Particle Interactions with S… [D]; OL28642487W Eastern Europe - Factors Underlying the… [D]; OL13604914W Anatomie de la sparation [D] |
| synthetic-mirror.v1-0341 | principles of accounting weygandt |  · Principles of Accounting / Jerry J. Weygandt | ambiguous (close_scores), target at #2 | OL1881226W Accounting Principles [A]; OL1881228W Principles of Accounting [A]; OL1881224W Principles of Financial Accounting [C] |
| synthetic-mirror.v1-0347 | cleory ramoaa and her moiher |  · Ramona and Her Mother / Beverly Cleary | none (weak_evidence) |  |
| synthetic-mirror.v1-0348 | murphy |  · author: Joseph Murphy | ambiguous (author_only) | OL89605W Diplomat among warriors [D]; OL40203474W Notebook [D]; OL24510507W Sullivan's Secret [D] |
| synthetic-mirror.v1-0358 | piper |  · author: H. Beam Piper | ambiguous (author_only), target at #2 | OL4721685W John Piper [D]; OL25936890W Genesis [D]; OL8810467W Temple Trouble [D] |
| synthetic-mirror.v1-0369 | hemigway deaoh in the afteroon |  · Death in the afternoon / Ernest Hemingway | none (weak_evidence) |  |
| synthetic-mirror.v1-0373 | usher |  · author: Shaun Usher | ambiguous (weak_evidence), target at #2 | OL41078W The Fall of the House of Usher [E]; OL20040586W Letters of note [D]; OL34682499W Usher [A] |
| synthetic-mirror.v1-0374 | kids coloring book |  · Coloring Book Dog / Timmy Time Timmy Time Books For Kids | ambiguous (near_title_author) | OL28396293W Christmas Coloring Book for Kids [C]; OL21892535W Christmas Kids Coloring Book Kids Color… [C]; OL30943173W Coloring Book for Kids [C] |
| synthetic-mirror.v1-0377 | mcdonald paul faebr srugeon |  · Paul Faber, surgeon / George MacDonald | ambiguous (author_only) | OL25185851W Paul Muldoon [D]; OL36023197W McDonald's Conveyancing Manual [D]; OL26683088W Chèvre D'or [D] |
| synthetic-mirror.v1-0380 | wlider the long witer |  · The Long Winter / Laura Ingalls Wilder | none (weak_evidence) |  |
| synthetic-mirror.v1-0382 | livinustone mary slesosr |  · Mary Slessor / W. P. Livingstone | ambiguous (author_only) | OL26823825W Hans Brinker, or the Silver Skates [D]; OL32530859W Frankenstein by Mary Shelley [D]; OL39718797W History of Mary Prince [D] |
| synthetic-mirror.v1-0385 | cotezee disgrae |  · Disgrace / J. M. Coetzee | none (weak_evidence) |  |
| synthetic-mirror.v1-0395 | haford a mieton hindbook |  · A Milton handbook / James Holly Hanford | ambiguous (author_only) | OL44372009W Histoire du procès du maréchal de camp … [D]; OL40173356W Origami Paper [E]; OL2522536W The orphan; or, The unhappy marriage [E] |
| synthetic-mirror.v1-0400 | mcneile bulldog drummond s |  · Bulldog Drummond's Third Round / H. C. McNeile | ambiguous (near_title_author), target at #3 | OL241909W Bulldog Drummond [C]; OL92777W Bulldog Drummond at bay [C]; OL92767W Bulldog Drummond's Third Round [C] |
| synthetic-mirror.v1-0403 | i by the king |  · By the King / England and Wales. Sovereign (1625-1649 : Charles I). | ambiguous (weak_evidence) | OL768669W The Coronation Order of King James I [E]; OL40664233W Idylls of the King [E]; OL24406447W Farmers of Forty Centuries; Or, Permane… [D] |
| synthetic-mirror.v1-0431 | rime |  · Rime / Francesco Petrarca | ambiguous (score_conflict) | OL93204W Rime [A]; OL36069543W Rime of the Ancient Mariner [E]; OL3602875W Rime [A] |
| synthetic-mirror.v1-0442 | autobiography |  · Autobiography / John Stuart Mill | ambiguous (score_conflict) | OL202909W An autobiography [A]; OL27201164W Autobiography [A]; OL29340960W Autobiography of a Yogi [E] |
| synthetic-mirror.v1-0443 | 2022 |  · author: Leseevo CA  2022 | ambiguous (author_only) | OL25493923W 2022 [D]; OL25502158W 2022 [D]; OL25528961W 2022 [D] |
| synthetic-mirror.v1-0444 | nsbit wet mgaic |  · Wet Magic / Edith Nesbit | none (weak_evidence) |  |
| synthetic-mirror.v1-0456 | stienbeck the wayawrd bus |  · The Wayward Bus / John Steinbeck | none (weak_evidence) |  |
| synthetic-mirror.v1-0462 | caorlina laws etc sesion laws 1795 nov |  · Laws, etc. (Session laws : 1795 Nov.) / North Carolina. | ambiguous (weak_evidence), target at #2 | OL14903581W Laws, etc [E]; OL1541042W Laws, etc. (Session laws : 1795 Nov.) [D]; OL10396878W Laws, etc [E] |
| synthetic-mirror.v1-0466 | my quotable patients the funniest things patients say |  · My Quotable Patients - the Funniest Things Patients Say / Nova Nova Bechtelar | ambiguous (close_scores), target at #4 | OL30938818W My Quotable Patients - the Funniest Thi… [A]; OL30938682W My Quotable Patients - the Funniest Thi… [A]; OL36153837W My Quotable Patients - the Funniest Thi… [A] |
| synthetic-mirror.v1-0470 | wolof nigth and day |  · Night and Day / Virginia Woolf | ambiguous (author_only) | OL26137410W Billionaire Romance [D]; OL30550487W Dreams in the Nigth [D]; OL20539621W The Nigth Land [E] |
| synthetic-mirror.v1-0471 | a set of six conrad |  · A set of six / Joseph Conrad | ambiguous (weak_evidence) | OL21757085W 6 : Aus [A]; OL24263506W Espace mathématique 6. [C]; OL8469612W Take 6 Ingredients [C] |
| synthetic-mirror.v1-0472 | blood pressure log book |  · Blood Pressure Log Book / med plann | ambiguous (near_title_author), target at #2 | OL30289635W Blood Pressure Log Book [C]; OL25523007W Blood Pressure Log Book [A]; OL30430489W Blood Pressure Log Book : Blood Pressur… [A] |
| synthetic-mirror.v1-0489 | king wizadr and galss |  · Wizard and Glass / Stephen King | ambiguous (author_only) | OL27121779W Three Kings Day [D]; OL27129197W Three Kings [D]; OL22241325W Life and Correspondence of Rufus King [D] |
| synthetic-mirror.v1-0490 | smiht the no 1 ladiis detectiev ageny |  · The No. 1 Ladies' Detective Agency / Alexander McCall Smith | ambiguous (author_only) | OL13726292W Skräcken [D]; OL43377922W Torn and Broken [D]; OL9424930W Practising Your English (Success at Sch… [D] |
| synthetic-mirror.v1-0491 | shakspeare wniter s tale by willima shakespeaae illustratd edition |  · Winter's Tale by William Shakespeare Illustrated Edition / William Shakespeare | ambiguous (author_only) | OL258636W The Beauties of Shakspeare [D]; OL21474187W Shakspeare's Dramatische Werke [D]; OL1093017W Palamon and Arcite; or, the Knight's ta… [E] |
| synthetic-mirror.v1-0492 | christo |  · Christo / Christo | none (weak_evidence) |  |
| synthetic-mirror.v1-0493 | publication |  · author: T. T. T. U Mahu Publication | ambiguous (close_scores) | OL253175W [Publications [A]; OL16823361W Publications [A]; OL13123706W Publication [A] |
| synthetic-mirror.v1-0494 | donizetoi luca di laimermoor |  · Lucia di Lammermoor / Gaetano Donizetti | ambiguous (author_only) | OL41194705W Il fotografo di Auschwitz [D]; OL24657422W Descrizione Delle Pitture a Fresco Di L… [D]; OL3762364W Mostra di Luca Signorelli [D] |
| synthetic-mirror.v1-0495 | rimage the alln baion guiae to writong |  · The Allyn & Bacon guide to writing / John D. Ramage | none (weak_evidence) |  |
| synthetic-mirror.v1-0498 | scrievn advunces in hetarocyclic cemistry |  · Advances in Heterocyclic Chemistry / Eric Scriven | none (weak_evidence) |  |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
