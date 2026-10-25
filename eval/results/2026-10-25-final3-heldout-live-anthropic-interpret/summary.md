# Eval: Find That Book HELD-OUT set v1 · final3-heldout-live-anthropic-interpret

> **HELD-OUT RESULTS.** Sealed set, final evaluation only. Nothing may be tuned on these rows.

> HELD-OUT RESULTS: sealed set, final evaluation only. Do not tune anything on these rows.

| Run | |
|---|---|
| Date | 2026-10-25 03:46 MT (-06:00) · 2026-10-25 05:46 ET (-04:00) |
| Code fingerprint (src/) | `sha256:918d7b88cc5867cf (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:bb794a4ff02178f3 (2 files)` |
| Eval tool fingerprint | `sha256:bfa0664db82124b7 (31 files)` |
| Dataset | Find That Book HELD-OUT set v1 (`eval/heldout.json`, sha256:b2570dcf3f70231a) |
| Held out | **yes** |
| Tuning | HELD-OUT (sealed): never tuned on; final evaluation only |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | anthropic |
| Models (observed in traces) | interpret: anthropic/claude-haiku-5-5 |
| Interpretations | live LLM |
| Explanations | template only (--explain template: the explain LLM is never called) |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 409 cases · 409 run · 0 skipped · 0 rejected by validation · 1 errors · 0 in-sample |
| Concurrency / limit | 2 / 5 · LLM paced to 45 calls/min |
| Wall time | 558.8 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set heldout --heldout --catalog live --provider anthropic --explain template --max-llm-calls 450 --quiet --label final3-heldout-live-anthropic-interpret` |
| Per-row results | `raw/results.jsonl` (409 rows; gitignored, kept locally) |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **285/302 = .944** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 331/406 = .815 | answerable rows, first shown candidate acceptable |
| Top-5 | 356/406 = .877 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 378/409 = .924 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 302/406 = .744 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 3 | any `match` where no book is right |
| Severe matches | 16 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 1 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| hand | 52 | 45/49 = .918 | 49/49 = 1.000 | 48/52 = .923 | 42 @ .905 | 0 |
| llm_author_character | 8 | 7/8 = .875 | 8/8 = 1.000 | 8/8 = 1.000 | 4 @ 1.000 | 0 |
| llm_half_remembered | 54 | 37/54 = .685 | 41/54 = .759 | 49/54 = .907 | 35 @ .943 | 0 |
| llm_misspeller | 61 | 40/61 = .656 | 41/61 = .672 | 52/61 = .852 | 38 @ .921 | 0 |
| llm_non_native | 52 | 48/52 = .923 | 51/52 = .981 | 50/52 = .962 | 45 @ .978 | 0 |
| llm_plot | 23 | 12/23 = .522 | 14/23 = .609 | 17/23 = .739 | 5 @ .200 | 0 |
| llm_rushed_mobile | 80 | 73/80 = .913 | 76/80 = .950 | 77/80 = .963 | 71 @ .986 | 0 |
| prog_drop | 8 | 4/8 = .500 | 8/8 = 1.000 | 7/8 = .875 | 1 @ .000 | 0 |
| prog_format | 16 | 16/16 = 1.000 | 16/16 = 1.000 | 16/16 = 1.000 | 15 @ 1.000 | 0 |
| prog_swap | 18 | 17/18 = .944 | 18/18 = 1.000 | 18/18 = 1.000 | 15 @ 1.000 | 0 |
| prog_typo | 22 | 17/22 = .773 | 19/22 = .864 | 21/22 = .955 | 16 @ .938 | 0 |
| prog_year | 15 | 15/15 = 1.000 | 15/15 = 1.000 | 15/15 = 1.000 | 15 @ 1.000 | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 0, template 1903 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 393

## Degraded

- Degraded rate: 393/408 = .963
  - of which 391 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 2/408 = .005
- interpretation_fallback: 2
- explanation_fallback: 393
- Interpretation source: llm 406, heuristic 2

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 409 | 1663 ms | 5669 ms | 16566 ms |
| Pipeline elapsed_ms | 408 | 1655 ms | 5604 ms | 16323 ms |
| stage `clean` | 408 | 0 ms | 0 ms | 4 ms |
| stage `explain` | 393 | 0 ms | 3 ms | 21 ms |
| stage `interpret` | 408 | 1273 ms | 2022 ms | 4013 ms |
| stage `judge` | 393 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 408 | 12 ms | 44 ms | 130 ms |
| stage `retrieve` | 408 | 1655 ms | 5602 ms | 16319 ms |
| Open Library call (not cache hits) | 798 | 407 ms | 2617 ms | 14414 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 408 | 1273 ms | 2022 ms | 4008 ms |

## Open Library calls

- Logical calls per query: mean 2.04, max 5, at budget 17 (budget: 5 logical calls)
- Cache hits: 33/831 = .040
- HTTP attempts: 800 total, mean 1.96 per query; slow-empty re-queries 0
- Outcomes: ok 831
- Steps: broad 408, hypothesis 216, any_of 89, original_title 33, person_fallback 33, person_fallback_any 25, person 8, author_works 6, suggestions 5, topic 5, person_any 3

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 393 | 0 | not_configured 393 | n/a | n/a |
| interpret | 408 | 0 | ok 406, timeout 2 | 556238 (2 unreported) | 40462 (2 unreported) |

- Paid LLM calls (not cache hits, incl. judge): 408
- Estimated cost (IAiPricing, paid-tier list prices): $0.075855 for 408 searches = **$0.185919 per 1,000 searches** (lower bound: 2 calls unpriced)

## Misses (76)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| ho-007 | pirate diary jake carpenter | match/ambiguous · Pirate Diary Journal of Jake Carpenter | none (weak_evidence) |  |
| ho-019 | Un London mieville | match/ambiguous · Un Lun Dun | ambiguous (author_only) | OL14942956W The Call of the Wild [D]; OL74502W The Iron Heel [D]; OL74504W White Fang [D] |
| ho-020 | mane traveled rodes | match/ambiguous · Main-travelled roads | none (weak_evidence) |  |
| ho-023 | Shakespeare's Tragedies Bradley | match/ambiguous · Shakespearean Tragedy | ambiguous (near_title_author), target at #5 | OL258902W Macbeth [C]; OL258850W Othello [C]; OL362702W Julius Caesar [C] |
| ho-036 | the federalist hamiltno | match/ambiguous · The Federalist, or, The New Constitution | match (exact_title_primary_author) | OL27105877W Federalist Papers [A]; OL26972971W Federalist Papers [A]; OL27156299W Federalist Papers Annotated [C] |
| ho-043 | The adventures about Captain Bonneville | match/ambiguous · The adventures of Captain Bonneville, U.S.A. | none (weak_evidence) |  |
| ho-056 | Little girl big DeWoskin | match/ambiguous · Big girl small | none (weak_evidence) |  |
| ho-058 | two dogs and a cat finding their way home | match/ambiguous · The Incredible Journey | match (exact_title_primary_author) | OL18825181W A Dog's Way Home [A]; OL54059W Roughing It [E]; OL29258988W A Dog's Way Home [A] |
| ho-065 | bulldog drummond at bay mnceile | match/ambiguous · Bulldog Drummond at bay | ambiguous (near_title_author), target at #2 | OL241909W Bulldog Drummond [C]; OL92777W Bulldog Drummond at bay [A]; OL92767W Bulldog Drummond's Third Round [C] |
| ho-071 | 1764 essay against torture and capital punishment | match/ambiguous · Dei delitte e delle pene | match (exact_title_primary_author) | OL43798230W Dei delitti e delle pene [A]; OL44735138W Dei delitti e delle pene del marchese C… [C]; OL43798202W Dei delitti e delle pene di Cesare Becc… [C] |
| ho-082 | Posies Lawrence poems | match/ambiguous · Pansies | ambiguous (author_only) | OL81294W Women in Love [D]; OL81205W Poems [D]; OL81180W Lady Chatterley's Lover [D] |
| ho-085 | Sweep jonathan oxier | match/ambiguous · Sweep | ambiguous (author_only) | OL20600W Gulliver's Travels [D]; OL4082696W The Corrections [D]; OL262438W The Sign of Four [E] |
| ho-092 | paetrson williiams | match/ambiguous · Paterson | ambiguous (author_only) | OL274433W The Classic Tale of the Velveteen Rabbit [D]; OL30294W A Streetcar Named Desire [D]; OL30293W The Glass Menagerie [D] |
| ho-095 | niven the ringworld engineers | match/ambiguous · The Ringworld Engineers (Ringworld) | ambiguous (author_only), target at #2 | OL510405W Ringworld [D]; OL510404W The Ringworld Engineers (Ringworld) [A]; OL510428W The Ringworld throne [D] |
| ho-097 | In Straynj Company Guy Boothbee | match/ambiguous · In strange company | ambiguous (unsupported_guess) | OL59688W Stranger in a Strange Land [A]; OL20119340W Starship Troopers and Stranger in a Str… [C]; OL15075590W Stranger in a Strange Land, Part 2 of 2 [C] |
| ho-101 | The Hearts Invisible Furees | match/ambiguous · The Heart's Invisible Furies | ambiguous (weak_evidence), target at #2 | OL52266W The Invisible Man [E]; OL19551794W The Heart's Invisible Furies [A]; OL38663W Heart of Darkness [E] |
| ho-109 | the night prior Christmas Moore | match/ambiguous · The Night Before Christmas | ambiguous (unsupported_guess), target at #2 | OL31900353W A visit from St. Nicholas [A]; OL655914W The night before Christmas, or, A visit… [C]; OL33850950W A Visit From St. Nicholas [A] |
| ho-120 | The Zepelin's Passinger | match/ambiguous · The Zeppelin's Passenger | ambiguous (author_only) | OL31702473W The valiant virgin; or, Phillip and Mary [D]; OL4009277W PPS in Formereien [D]; OL42321607W The Country gentleman, or, The Happy li… [D] |
| ho-124 | Bulldog Drummond in bay | match/ambiguous · Bulldog Drummond at bay | match (exact_title), target at #2 | OL241909W Bulldog Drummond [A]; OL92777W Bulldog Drummond at bay [A]; OL92767W Bulldog Drummond's Third Round [E] |
| ho-128 | Czech drama about manufactured workers rebelling against humans | match/ambiguous · R.U.R. and The insect play | ambiguous (topic_search), target at #5 | OL46125W Foundation [E]; OL46241W I, Robot [E]; OL46401W The Caves of Steel [E] |
| ho-143 | black amazon mars | match/ambiguous · Black Amazon of Mars | ambiguous (author_only), target at #3 | OL53908W Adventures of Huckleberry Finn [D]; OL28577W The Souls of Black Folk [E]; OL40098911W Black Amazon of Mars [A] |
| ho-152 | vieu souvenirs du prince de joinvil | match/ambiguous · Vieux souvenirs de Mgr le prince de Joinville, 1818-1848 | error (catalog_timeout) |  |
| ho-154 | England and the English from an Amerikan point of view | match/ambiguous · England and the English from an American point of view | none (weak_evidence) |  |
| ho-158 | future society novel responding to Orwell | match/ambiguous · 1985 | ambiguous (author_only) | OL1168083W Nineteen Eighty-Four [D]; OL1168007W Animal Farm [D]; OL1168169W Homage to Catalonia [D] |
| ho-163 | Shatterd Dick Fransis | match/ambiguous · Shattered | ambiguous (author_only) | OL2172356W Do Androids Dream of Electric Sheep? [D]; OL2172403W The Man in the High Castle [D]; OL102749W Moby Dick [E] |
| ho-168 | Russian writer nonfiction account of Soviet prison camps | match/ambiguous · Архипелаг ГУЛАГ | none (weak_evidence) |  |
| ho-175 | Half Servings Ferber | match/ambiguous · Half Portions | ambiguous (author_only), target at #2 | OL2646769W So Big [D]; OL2646760W Half Portions [D]; OL82565W Harry Potter and the Half-Blood Prince [E] |
| ho-182 | free | match/ambiguous · The Free Fishers | ambiguous (weak_evidence), target at #3 | OL51142W Free air [E]; OL52257W The World Set Free [E]; OL76573W The Free Fishers [E] |
| ho-183 | portions | match/ambiguous · Half Portions | ambiguous (weak_evidence), target at #4 | OL4304829W Food values of portions commonly used [E]; OL507381W The portion of labor [E]; OL1535782W Memoirs of John Quincy Adams [E] |
| ho-186 | jane eyre wide sargasso | match/ambiguous · title | match (exact_title), target at #3 | OL1095427W Jane Eyre [A]; OL32417366W Jane Eyre [B]; OL1858668W Wide Sargasso Sea [E] |
| ho-189 | sord of flame maggie fury | match/ambiguous · Sword of Flame | ambiguous (weak_evidence) | OL20839W Maggie, a girl of the streets [E]; OL82870W The Sound and the Fury [E]; OL17860744W A Court of Mist and Fury [E] |
| ho-195 | history england letters | match/ambiguous · A history of England in a series of letters from a nobleman to his son | ambiguous (author_only) | OL769120W Book of common prayer [D]; OL9250413W A Child's History of England [E]; OL93567W The history of England [E] |
| ho-196 | Critical works of James Joyce | match/ambiguous · The critical writings of James Joyce | ambiguous (author_only) | OL86320W Dubliners [D]; OL43024W As a Man Thinketh [D]; OL77958W The Last of the Mohicans [D] |
| ho-202 | The Mystery Rider Zane Grey | match/ambiguous · The Mysterious Rider | ambiguous (author_only), target at #2 | OL485504W Riders of the Purple Sage [D]; OL485697W The Mysterious Rider [D]; OL17448W King Solomon's Mines [D] |
| ho-205 | achilles anger during siege of troy | match/ambiguous · Ἰλιάς | match (exact_title_primary_author) | OL40715510W Iliad [A]; OL35743338W The Iliad [B]; OL40678138W Iliad of Homer [C] |
| ho-220 | sherlock holmes | ambiguous · author | match (exact_title), target at #1 | OL262496W A Study in Scarlet [A]; OL262426W The Case-Book of Sherlock Holmes [E]; OL69612W The Secret Garden [E] |
| ho-221 | The Beest Must Dye | match/ambiguous · The Beast Must Die | none (weak_evidence) |  |
| ho-225 | rice man steps arnold bennett | match/ambiguous · Riceyman Steps | ambiguous (unsupported_guess) | OL109236W The old wives' tale [A]; OL109147W The Old Wives\' Tale Volume I [A]; OL109146W The Old Wives' Tale Volume II [A] |
| ho-227 | Rewind Creech | match/ambiguous · Replay | ambiguous (author_only) | OL14937643W Walk Two Moons [D]; OL31545W Love That Dog [D]; OL31551W The Wanderer [D] |
| ho-228 | Hans Fall by Edgar Allen Po | match/ambiguous · The Unparalleled Adventure of One Hans Pfaall | match (exact_title_primary_author) | OL41078W The Fall of the House of Usher [A]; OL273484W The Fall of the House of Usher and Othe… [A]; OL41041W Tales of the Grotesque and Arabesque [D] |
| ho-230 | Little Wizerd Stories of Oz | match/ambiguous · Little Wizard stories of Oz | ambiguous (weak_evidence) | OL18417W The Wonderful Wizard of Oz [E]; OL29983W Little Women [E]; OL18412W Dorothy and the Wizard in Oz [E] |
| ho-232 | Portrait of Chekhov | match/ambiguous · The image of Chekhov | ambiguous (weak_evidence) | OL55496W Note-Book of Anton Chekhov [E]; OL55417W Вишневый сад [D]; OL15214506W Chekhov [E] |
| ho-244 | biographical essays about Plato Swedenborg Montaigne Napoleon | match/ambiguous · Representative men | ambiguous (topic_search) | OL51784W Εὐθύφρων / Κρίτων / Φαίδων / Ἀπολογία Σ… [D]; OL51831W πολιτεία [D]; OL51815W Συμπόσιον [D] |
| ho-245 | A Good Yeer Peter Mail | match/ambiguous · A good year | match (exact_title_primary_author) | OL80512W A year in Provence [A]; OL9351036W A Year In Provence [A]; OL21205663W My Twenty-Five Years in Provence [C] |
| ho-249 | kig rat meville | match/ambiguous · King Rat | ambiguous (unsupported_guess), target at #3 | OL102749W Moby Dick [A]; OL26032991W Moby Dick by Herman Melville [C]; OL14871882W King Rat [D] |
| ho-255 | Engineers of Ringworld Larry Niven | match/ambiguous · The Ringworld Engineers (Ringworld) | ambiguous (author_only), target at #2 | OL510405W Ringworld [D]; OL510404W The Ringworld Engineers (Ringworld) [A]; OL510428W The Ringworld throne [D] |
| ho-268 | The Demon Doctor Rohmer | match/ambiguous · The Devil Doctor | ambiguous (author_only) | OL2288684W The Insidious Dr. Fu Manchu [D]; OL2288676W Brood of the Witch-Queen [D]; OL2288675W The Hand of Fu-Manchu [D] |
| ho-271 | dei delitti e delle peni beccaria | match/ambiguous · Dei delitte e delle pene | match (exact_title_primary_author) | OL43798230W Dei delitti e delle pene [A]; OL44735138W Dei delitti e delle pene del marchese C… [C]; OL43798202W Dei delitti e delle pene di Cesare Becc… [C] |
| ho-273 | A fewg in time | match/ambiguous · A fugue in time | match (exact_title) | OL37975649W Few Days at a Time [A]; OL6393708W A few days in my time [E]; OL5362893W The birth-day; with a few strictures on… [E] |
| ho-276 | wolfville nigths | match/ambiguous · Wolfville nights | none (weak_evidence) |  |
| ho-282 | War Cross Marie Loo | match/ambiguous · Warcross | ambiguous (weak_evidence) | OL244537W The Art of War [E]; OL267171W War and Peace [E]; OL52114W The War of the Worlds [E] |
| ho-285 | The Empty House and Other Tales | match/ambiguous · The Empty House and Other Ghost Stories | none (weak_evidence) |  |
| ho-287 | war and peace tolstoy | match/ambiguous · title | match (exact_title_primary_author), target at #3 | OL267171W War and Peace [A]; OL267096W Анна Каренина [D]; OL45017724W War and Peace [A] |
| ho-295 | Down the Road Kerouac | match/ambiguous · On The Road | ambiguous (unsupported_guess) | OL65936W Desolation angels [A]; OL31384318W Desolation Angels, Famous Underground N… [D]; OL22643705W American road literature [E] |
| ho-298 | rider grey | match/ambiguous · The Mysterious Rider | ambiguous (near_title_author), target at #2 | OL485504W Riders of the Purple Sage [C]; OL485697W The Mysterious Rider [C]; OL485423W The Desert of Wheat [D] |
| ho-304 | la nouvel eloiz russo | match/ambiguous · La nouvelle Héloïse | ambiguous (close_scores) | OL15250122W Julie ou La nouvelle Heloïse [A]; OL43243008W Julie Ou La Nouvelle Heloise [A]; OL43970639W Julie; ou, La nouvelle Héloïse [A] |
| ho-306 | wrinkle in time graphic novel | match/ambiguous · title | ambiguous (weak_evidence), target at #2 | OL17192857W Madeline L'Engle's A Wrinkle in Time-Th… [E]; OL19360587W A Wrinkle In Time [E]; OL41495W A Wrinkle in Time [A] |
| ho-308 | she comis first kyrner | match/ambiguous · She Comes First | ambiguous (unsupported_guess) | OL17077562W Shelter [A]; OL74684W Shelter [A]; OL20027528W Shelter [A] |
| ho-311 | saunders of river | match/ambiguous · Sanders of the river | ambiguous (author_only) | OL20938W The Mill on the Floss [D]; OL21026339W Rivers [A]; OL17635834W Lincoln in the Bardo [D] |
| ho-315 | Mortal Frends Jane Stanton Hitchcok | match/ambiguous · Mortal Friends | ambiguous (author_only) | OL66554W Pride and Prejudice [D]; OL66513W Emma [D]; OL66562W Sense and Sensibility [D] |
| ho-318 | The Bishop mystery | match/ambiguous · The Bishop murder case | none (weak_evidence) |  |
| ho-320 | sandworms dune | match/ambiguous · Sandworms of Dune | match (exact_title_primary_author) | OL893414W Dune [A]; OL893461W Dune Messiah [C]; OL893516W Children of Dune [C] |
| ho-326 | master and margarita bulgakov | match/ambiguous · title | match (exact_title_primary_author), target at #2 | OL676009W Мастер и Маргарита [A]; OL40220376W Master I Margarita [C]; OL45435048W Master i Margarita [C] |
| ho-333 | paradiso | match/ambiguous · Paradiso | ambiguous (near_title_author), target at #2 | OL93230W Il Paradiso [C]; OL44237703W Paradiso [A]; OL810991W Paradise Lost [E] |
| ho-345 | dune anderson | match/ambiguous · Sandworms of Dune | match (exact_title_primary_author), target at #3 | OL27776707W Dune [A]; OL19618275W Dune [A]; OL14961045W Sandworms of Dune [C] |
| ho-353 | The Black Flame Weinbowm | match/ambiguous · The black flame | none (weak_evidence) |  |
| ho-360 | Leaving the Atocha Stayshun | match/ambiguous · Leaving the Atocha Station | none (weak_evidence) |  |
| ho-363 | The Heavenly Servant Bunyan | match/ambiguous · Heavenly footman | ambiguous (author_only), target at #2 | OL107195W The Pilgrim's Progress [D]; OL107193W Heavenly footman [D]; OL107194W Grace abounding to the chief of sinners [D] |
| ho-367 | white worm illustrated stoker | match/ambiguous · Lair of the White Worm Illustrated | ambiguous (near_title_author), target at #3 | OL85875W Bram Stoker's Dracula omnibus [C]; OL29827658W Lair of the White Worm [A]; OL26825776W Lair of the White Worm Illustrated [C] |
| ho-370 | book of five circles musashi | match/ambiguous · Gorin no sho | match (exact_title_primary_author) | OL39568417W The Book of Five Rings [A]; OL37791428W A Book of Five Rings [A]; OL38366815W Book of Five Rings and Dokkodo [C] |
| ho-382 | Sykee Erwin Rode | match/ambiguous · Psyche | ambiguous (author_only) | OL3246392W The Audacity of Hope [D]; OL1210514W What is life? The physical aspect of th… [D]; OL25821897W Toy Box [D] |
| ho-385 | girl follows magic thread to rescue miner boy | match/ambiguous · The Princess and the Goblin | ambiguous (weak_evidence) | OL45887W Boy [E]; OL453657W The Colour of Magic [E]; OL5840870W The Boy in the Striped Pyjamas [E] |
| ho-390 | Anne Beddingfeld mystery adventure | match/ambiguous · The Man in the Brown Suit | match (exact_title_primary_author), target at #2 | OL472542W The Secret of Chimneys [A]; OL472549W The Man in the Brown Suit [D]; OL472261W Sparkling Cyanide; The Secret of Chimne… [D] |
| ho-405 | two children recovering after a sledding accident | match/ambiguous · Jack and Jill | none (weak_evidence) |  |
| ho-406 | Racine Nero | match/ambiguous · Britannicus | ambiguous (author_only), target at #4 | OL19922727W Phèdre [D]; OL503424W Quo Vadis? Powieść z czasów Nerona [E]; OL741414W Théâtre complet [D] |
| ho-407 | story about a dog in belgium | match/ambiguous · A Dog of Flanders | ambiguous (topic_search) | OL14942956W The Call of the Wild [E]; OL15572356W A Dog's Purpose [E]; OL5682519W Dog Days [E] |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
