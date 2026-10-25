# Eval: Find That Book HELD-OUT set v1 · final3-heldout-live-none

> **HELD-OUT RESULTS.** Sealed set, final evaluation only. Nothing may be tuned on these rows.

> HELD-OUT RESULTS: sealed set, final evaluation only. Do not tune anything on these rows.

| Run | |
|---|---|
| Date | 2026-10-25 03:30 MT (-06:00) · 2026-10-25 05:30 ET (-04:00) |
| Code fingerprint (src/) | `sha256:918d7b88cc5867cf (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:bb794a4ff02178f3 (2 files)` |
| Eval tool fingerprint | `sha256:bfa0664db82124b7 (31 files)` |
| Dataset | Find That Book HELD-OUT set v1 (`eval/heldout.json`, sha256:b2570dcf3f70231a) |
| Held out | **yes** |
| Tuning | HELD-OUT (sealed): never tuned on; final evaluation only |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | none |
| Models (observed in traces) | none (no LLM call reached a provider) |
| Interpretations | none (LLM off) |
| Explanations | template (LLM off) |
| Ranker | `default` |
| Judge | off |
| Caches | as configured (cold at process start) |
| Counts | 409 cases · 409 run · 0 skipped · 0 rejected by validation · 1 errors · 0 in-sample |
| Concurrency / limit | 2 / 5 |
| Wall time | 382.0 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set heldout --heldout --catalog live --provider none --quiet --label final3-heldout-live-none` |
| Per-row results | `raw/results.jsonl` (409 rows; gitignored, kept locally) |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **223/227 = .982** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 295/406 = .727 | answerable rows, first shown candidate acceptable |
| Top-5 | 330/406 = .813 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 366/409 = .895 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 227/406 = .559 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 3 | any `match` where no book is right |
| Severe matches | 4 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 1 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| hand | 52 | 43/49 = .878 | 47/49 = .959 | 50/52 = .962 | 34 @ .941 | 0 |
| llm_author_character | 8 | 5/8 = .625 | 8/8 = 1.000 | 8/8 = 1.000 | 0 @ n/a | 0 |
| llm_half_remembered | 54 | 34/54 = .630 | 43/54 = .796 | 50/54 = .926 | 18 @ 1.000 | 0 |
| llm_misspeller | 61 | 24/61 = .393 | 33/61 = .541 | 48/61 = .787 | 11 @ 1.000 | 0 |
| llm_non_native | 52 | 45/52 = .865 | 47/52 = .904 | 49/52 = .942 | 38 @ .974 | 0 |
| llm_plot | 23 | 2/23 = .087 | 4/23 = .174 | 9/23 = .391 | 0 @ n/a | 0 |
| llm_rushed_mobile | 80 | 73/80 = .913 | 73/80 = .913 | 77/80 = .963 | 64 @ 1.000 | 0 |
| prog_drop | 8 | 4/8 = .500 | 8/8 = 1.000 | 7/8 = .875 | 1 @ .000 | 0 |
| prog_format | 16 | 16/16 = 1.000 | 16/16 = 1.000 | 16/16 = 1.000 | 15 @ 1.000 | 0 |
| prog_swap | 18 | 17/18 = .944 | 18/18 = 1.000 | 18/18 = 1.000 | 16 @ 1.000 | 0 |
| prog_typo | 22 | 17/22 = .773 | 18/22 = .818 | 19/22 = .864 | 15 @ 1.000 | 0 |
| prog_year | 15 | 15/15 = 1.000 | 15/15 = 1.000 | 15/15 = 1.000 | 15 @ 1.000 | 0 |

## Judge

Off in this run (n/a).

## Explanations

- Source mix (shown candidates): llm 0, template 1808 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 368

## Degraded

- Degraded rate: 408/408 = 1.000
  - of which 408 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 0/408 = .000
- ai_not_configured: 408
- explanation_fallback: 368
- Interpretation source: heuristic 408

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 409 | 1093 ms | 5635 ms | 15242 ms |
| Pipeline elapsed_ms | 408 | 1081 ms | 5604 ms | 9942 ms |
| stage `clean` | 408 | 0 ms | 0 ms | 4 ms |
| stage `explain` | 368 | 0 ms | 6 ms | 24 ms |
| stage `interpret` | 408 | 0 ms | 0 ms | 8 ms |
| stage `judge` | 368 | 0 ms | 0 ms | 0 ms |
| stage `rank` | 408 | 11 ms | 60 ms | 181 ms |
| stage `retrieve` | 408 | 1079 ms | 5585 ms | 9942 ms |
| Open Library call (not cache hits) | 742 | 560 ms | 3696 ms | 9932 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 0 | n/a | n/a | n/a |

## Open Library calls

- Logical calls per query: mean 1.84, max 4, at budget 0 (budget: 5 logical calls)
- Cache hits: 10/752 = .013
- HTTP attempts: 744 total, mean 1.82 per query; slow-empty re-queries 0
- Outcomes: ok 752
- Steps: broad 408, any_of 189, person_fallback 81, person_fallback_any 67, author_works 5, topic 2

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 368 | 0 | not_configured 368 | n/a | n/a |
| interpret | 408 | 0 | not_configured 408 | n/a | n/a |

- Paid LLM calls (not cache hits, incl. judge): 0
- Estimated cost: n/a (no priced calls)

## Misses (111)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| ho-007 | pirate diary jake carpenter | match/ambiguous · Pirate Diary Journal of Jake Carpenter | none (weak_evidence) |  |
| ho-010 | Three Ginnies Virginia Wolf | match/ambiguous · Three guineas | ambiguous (author_only) | OL39396W To the Lighthouse [D]; OL39349W Mrs. Dalloway [D]; OL39360W Orlando [D] |
| ho-014 | Hekuba by Yuripides | match/ambiguous · Hecuba | none (weak_evidence) |  |
| ho-019 | Un London mieville | match/ambiguous · Un Lun Dun | ambiguous (author_only) | OL14942956W The Call of the Wild [D]; OL74502W The Iron Heel [D]; OL74504W White Fang [D] |
| ho-020 | mane traveled rodes | match/ambiguous · Main-travelled roads | none (weak_evidence) |  |
| ho-023 | Shakespeare's Tragedies Bradley | match/ambiguous · Shakespearean Tragedy | ambiguous (author_only) | OL9170454W Hamlet [D]; OL258902W Macbeth [D]; OL258850W Othello [D] |
| ho-025 | King Rat by China Meevil | match/ambiguous · King Rat | ambiguous (author_only), target at #3 | OL81613W It [D]; OL81626W Carrie [D]; OL14871882W King Rat [C] |
| ho-028 | three guinas virginia woolf | match/ambiguous · Three guineas | ambiguous (author_only) | OL39396W To the Lighthouse [D]; OL39349W Mrs. Dalloway [D]; OL39360W Orlando [D] |
| ho-031 | the naturel bernard malenud | match/ambiguous · The natural | ambiguous (author_only), target at #3 | OL1066524W Pygmalion [D]; OL1066526W Man and Superman [D]; OL1937853W The natural [D] |
| ho-040 | captain bonaville travel | match/ambiguous · The adventures of Captain Bonneville, U.S.A. | none (weak_evidence) |  |
| ho-041 | Jonny Tremaine | match/ambiguous · Johnny Tremain | ambiguous (weak_evidence), target at #5 | OL16478203W The Masqueraders [E]; OL5694906W Saving Dr. Tremaine [E]; OL14851646W Across the years [E] |
| ho-047 | wolfvilee nights leewis | match/ambiguous · Wolfville nights | none (weak_evidence) |  |
| ho-050 | russian married woman has affair then dies under train | match/ambiguous · Анна Каренина | none (weak_evidence) |  |
| ho-052 | lobel frog | match/ambiguous · Frog and Toad Are Friends | ambiguous (near_title_author), target at #2 | OL1973422W Frog and Toad Together [C]; OL449277W Frog and Toad Are Friends [D]; OL1973442W Frog and Toad All Year [D] |
| ho-053 | trojan hero escapes burning city and founds rome | match/ambiguous · Aeneis | none (weak_evidence) |  |
| ho-056 | Little girl big DeWoskin | match/ambiguous · Big girl small | none (weak_evidence) |  |
| ho-058 | two dogs and a cat finding their way home | match/ambiguous · The Incredible Journey | none (weak_evidence) |  |
| ho-062 | The Prairy by Cooper | match/ambiguous · The Prairie | ambiguous (author_only), target at #3 | OL77958W The Last of the Mohicans [D]; OL77950W The deerslayer [D]; OL77970W The Prairie [D] |
| ho-065 | bulldog drummond at bay mnceile | match/ambiguous · Bulldog Drummond at bay | ambiguous (near_title_author), target at #2 | OL241909W Bulldog Drummond [C]; OL92777W Bulldog Drummond at bay [A]; OL92767W Bulldog Drummond's Third Round [C] |
| ho-069 | the battul of life charles dickins | match/ambiguous · The Battle of Life | ambiguous (author_only), target at #2 | OL32466W A Christmas Carol [D]; OL14868497W The Battle of Life [D]; OL8193465W A Tale of Two Cities [D] |
| ho-071 | 1764 essay against torture and capital punishment | match/ambiguous · Dei delitte e delle pene | none (weak_evidence) |  |
| ho-077 | baseball novel about Roy Hobbs | match/ambiguous · The natural | ambiguous (author_only) | OL1052133W The God of Small Things [D]; OL362691W Much Ado About Nothing [E]; OL5591341W Wild life in the far West [D] |
| ho-082 | Posies Lawrence poems | match/ambiguous · Pansies | ambiguous (author_only) | OL81294W Women in Love [D]; OL81205W Poems [D]; OL81180W Lady Chatterley's Lover [D] |
| ho-085 | Sweep jonathan oxier | match/ambiguous · Sweep | ambiguous (author_only) | OL20600W Gulliver's Travels [D]; OL4082696W The Corrections [D]; OL262438W The Sign of Four [E] |
| ho-092 | paetrson williiams | match/ambiguous · Paterson | ambiguous (author_only) | OL40006698W Plugged for Life [D]; OL6582139W Old time Tazewell [E]; OL16803600W Башкурдский язык и Санскрит [E] |
| ho-095 | niven the ringworld engineers | match/ambiguous · The Ringworld Engineers (Ringworld) | ambiguous (author_only), target at #2 | OL510405W Ringworld [D]; OL510404W The Ringworld Engineers (Ringworld) [A]; OL510428W The Ringworld throne [D] |
| ho-097 | In Straynj Company Guy Boothbee | match/ambiguous · In strange company | none (weak_evidence) |  |
| ho-098 | fantastico george macdonal | match/ambiguous · Phantastes | ambiguous (author_only) | OL1168083W Nineteen Eighty-Four [D]; OL1168007W Animal Farm [D]; OL15449W The Princess and the Goblin [D] |
| ho-101 | The Hearts Invisible Furees | match/ambiguous · The Heart's Invisible Furies | none (weak_evidence) |  |
| ho-104 | a dog of flanders by Weeda | match/ambiguous · A Dog of Flanders | none (weak_evidence) |  |
| ho-114 | The Just Men of Cordova Edgar Wallis | match/ambiguous · The just men of Cordova | ambiguous (author_only), target at #2 | OL1517879W The Four Just Men [D]; OL1517988W The just men of Cordova [C]; OL41072W The Murders in the Rue Morgue [D] |
| ho-117 | Bracebridge House Irving | match/ambiguous · Bracebridge Hall | ambiguous (author_only), target at #2 | OL63985W The Legend of Sleepy Hollow [D]; OL63915W Bracebridge Hall [D]; OL63996W The Alhambra [D] |
| ho-120 | The Zepelin's Passinger | match/ambiguous · The Zeppelin's Passenger | ambiguous (author_only) | OL31702473W The valiant virgin; or, Phillip and Mary [D]; OL4009277W PPS in Formereien [D]; OL42321607W The Country gentleman, or, The Happy li… [D] |
| ho-128 | Czech drama about manufactured workers rebelling against humans | match/ambiguous · R.U.R. and The insect play | ambiguous (author_only) | OL2239396W À rebours [D]; OL505781W Of Human Bondage [E]; OL362691W Much Ado About Nothing [E] |
| ho-134 | La petite princesse | match/ambiguous · A Little Princess | ambiguous (close_scores) | OL8019128W La petite princesse [A]; OL16442546W The Duke is Mine [E]; OL25433W Princess and the Pea [E] |
| ho-138 | sailor memoir aboard brig Pilgrim | match/ambiguous · Two years before the mast | none (weak_evidence) |  |
| ho-143 | black amazon mars | match/ambiguous · Black Amazon of Mars | ambiguous (author_only) | OL53908W Adventures of Huckleberry Finn [D]; OL28577W The Souls of Black Folk [E]; OL15854658W Black Beauty [E] |
| ho-152 | vieu souvenirs du prince de joinvil | match/ambiguous · Vieux souvenirs de Mgr le prince de Joinville, 1818-1848 | error (catalog_timeout) |  |
| ho-154 | England and the English from an Amerikan point of view | match/ambiguous · England and the English from an American point of view | none (weak_evidence) |  |
| ho-158 | future society novel responding to Orwell | match/ambiguous · 1985 | ambiguous (author_only) | OL1168083W Nineteen Eighty-Four [D]; OL1168007W Animal Farm [D]; OL1168169W Homage to Catalonia [D] |
| ho-159 | incredible journey with animals | match/ambiguous · The Incredible Journey | ambiguous (weak_evidence), target at #2 | OL19645826W Incredible Journeys: Amazing Animal Mig… [A]; OL2562769W The Incredible Journey [E]; OL16282993W Animal planet--incredible journeys [C] |
| ho-160 | metal man searches for his lost sweetheart | match/ambiguous · The Tin Woodman of Oz | none (weak_evidence) |  |
| ho-163 | Shatterd Dick Fransis | match/ambiguous · Shattered | ambiguous (author_only) | OL2172356W Do Androids Dream of Electric Sheep? [D]; OL2172403W The Man in the High Castle [D]; OL102749W Moby Dick [E] |
| ho-165 | death row prisoner healing powers pet mouse | match/ambiguous · The Green Mile | none (weak_evidence) |  |
| ho-168 | Russian writer nonfiction account of Soviet prison camps | match/ambiguous · Архипелаг ГУЛАГ | none (weak_evidence) |  |
| ho-172 | Le phénomène humain | match/ambiguous · The Phenomenon of Man | match (exact_title), target at #2 | OL459264W Le phénomène humain [A]; OL459280W The Phenomenon of Man [E]; OL33965773W Le phénomène humain [A] |
| ho-175 | Half Servings Ferber | match/ambiguous · Half Portions | ambiguous (author_only), target at #2 | OL2646769W So Big [D]; OL2646760W Half Portions [D]; OL82565W Harry Potter and the Half-Blood Prince [E] |
| ho-179 | kidnapt robert louis stevenson | match/ambiguous · Kidnapped | ambiguous (author_only), target at #3 | OL24034W Treasure Island [D]; OL24156W The Strange Case of Dr. Jekyll and Mr. … [D]; OL24166W Kidnapped [D] |
| ho-180 | The Last Days of Pompay | match/ambiguous · The Last Days of Pompeii | none (weak_evidence) |  |
| ho-182 | free | match/ambiguous · The Free Fishers | ambiguous (weak_evidence), target at #3 | OL51142W Free air [E]; OL52257W The World Set Free [E]; OL76573W The Free Fishers [E] |
| ho-183 | portions | match/ambiguous · Half Portions | ambiguous (weak_evidence), target at #4 | OL4304829W Food values of portions commonly used [E]; OL507381W The portion of labor [E]; OL1535782W Memoirs of John Quincy Adams [E] |
| ho-186 | jane eyre wide sargasso | match/ambiguous · title | ambiguous (author_only) | OL66554W Pride and Prejudice [D]; OL66513W Emma [D]; OL66562W Sense and Sensibility [D] |
| ho-189 | sord of flame maggie fury | match/ambiguous · Sword of Flame | ambiguous (weak_evidence) | OL20839W Maggie, a girl of the streets [E]; OL82870W The Sound and the Fury [E]; OL17860744W A Court of Mist and Fury [E] |
| ho-190 | Portraits of Fidelman | match/ambiguous · Pictures of Fidelman | none (weak_evidence) |  |
| ho-195 | history england letters | match/ambiguous · A history of England in a series of letters from a nobleman to his son | ambiguous (author_only) | OL769120W Book of common prayer [D]; OL9250413W A Child's History of England [E]; OL93567W The history of England [E] |
| ho-196 | Critical works of James Joyce | match/ambiguous · The critical writings of James Joyce | ambiguous (author_only) | OL86320W Dubliners [D]; OL43024W As a Man Thinketh [D]; OL77958W The Last of the Mohicans [D] |
| ho-202 | The Mystery Rider Zane Grey | match/ambiguous · The Mysterious Rider | ambiguous (author_only), target at #2 | OL485504W Riders of the Purple Sage [D]; OL485697W The Mysterious Rider [D]; OL17448W King Solomon's Mines [D] |
| ho-205 | achilles anger during siege of troy | match/ambiguous · Ἰλιάς | none (weak_evidence) |  |
| ho-216 | man shipwrecked on island of tiny people | match/ambiguous · Gulliver's Travels | none (weak_evidence) |  |
| ho-217 | chief legaty | match/ambiguous · The Chief Legatee | ambiguous (weak_evidence) | OL472165W Peril at End House [E]; OL17610986W Dog Man [E]; OL4078156W The Scottish Chiefs [E] |
| ho-219 | three men on the bummul | match/ambiguous · Three Men on the Bummel | ambiguous (weak_evidence) | OL1793164W Three Men in a Boat (to say nothing of … [E]; OL27482W The Hobbit [E]; OL17761452W Little men [E] |
| ho-221 | The Beest Must Dye | match/ambiguous · The Beast Must Die | none (weak_evidence) |  |
| ho-227 | Rewind Creech | match/ambiguous · Replay | ambiguous (author_only) | OL14937643W Walk Two Moons [D]; OL31545W Love That Dog [D]; OL31551W The Wanderer [D] |
| ho-228 | Hans Fall by Edgar Allen Po | match/ambiguous · The Unparalleled Adventure of One Hans Pfaall | ambiguous (author_only) | OL43024W As a Man Thinketh [D]; OL41078W The Fall of the House of Usher [D]; OL41072W The Murders in the Rue Morgue [D] |
| ho-230 | Little Wizerd Stories of Oz | match/ambiguous · Little Wizard stories of Oz | ambiguous (weak_evidence), target at #3 | OL18417W The Wonderful Wizard of Oz [E]; OL29983W Little Women [E]; OL18394W Little Wizard stories of Oz [A] |
| ho-232 | Portrait of Chekhov | match/ambiguous · The image of Chekhov | ambiguous (weak_evidence) | OL55496W Note-Book of Anton Chekhov [E]; OL55417W Вишневый сад [D]; OL15214506W Chekhov [E] |
| ho-244 | biographical essays about Plato Swedenborg Montaigne Napoleon | match/ambiguous · Representative men | ambiguous (author_only) | OL1086528W Essais [D]; OL51831W πολιτεία [D]; OL527464W Think and Grow Rich [D] |
| ho-245 | A Good Yeer Peter Mail | match/ambiguous · A good year | none (weak_evidence) |  |
| ho-249 | kig rat meville | match/ambiguous · King Rat | none (weak_evidence) |  |
| ho-255 | Engineers of Ringworld Larry Niven | match/ambiguous · The Ringworld Engineers (Ringworld) | ambiguous (author_only), target at #2 | OL510405W Ringworld [D]; OL510404W The Ringworld Engineers (Ringworld) [A]; OL510428W The Ringworld throne [D] |
| ho-257 | Paterson poetry from William Carlos Williams | match/ambiguous · Paterson | ambiguous (author_only) | OL9170454W Hamlet [D]; OL258902W Macbeth [D]; OL258850W Othello [D] |
| ho-259 | song of ice and fire book 1 | match/ambiguous · title | ambiguous (weak_evidence), target at #4 | OL1955906W A Dance With Dragons [E]; OL5735360W Catching Fire [E]; OL5714289W Fire and Ice [E] |
| ho-260 | story about vincent van gogh | match/ambiguous · Lust for Life | ambiguous (topic_search), target at #2 | OL518262W Vincent van Gogh [D]; OL112003W Lust for Life [E]; OL518259W The letters of Vincent van Gogh [D] |
| ho-265 | caballero olmedo | match/ambiguous · El caballero de Olmedo | none (weak_evidence) |  |
| ho-268 | The Demon Doctor Rohmer | match/ambiguous · The Devil Doctor | ambiguous (author_only) | OL2288684W The Insidious Dr. Fu Manchu [D]; OL2288676W Brood of the Witch-Queen [D]; OL2288675W The Hand of Fu-Manchu [D] |
| ho-270 | the little prince | match/ambiguous · title | ambiguous (weak_evidence), target at #2 | OL1089297W The Prince [E]; OL10263W Le petit prince [E]; OL29983W Little Women [E] |
| ho-273 | A fewg in time | match/ambiguous · A fugue in time | ambiguous (weak_evidence) | OL52267W The Time Machine [E]; OL8193387W Hard Times [E]; OL44986W Jude the Obscure [E] |
| ho-282 | War Cross Marie Loo | match/ambiguous · Warcross | none (weak_evidence) |  |
| ho-285 | The Empty House and Other Tales | match/ambiguous · The Empty House and Other Ghost Stories | none (weak_evidence) |  |
| ho-287 | war and peace tolstoy | match/ambiguous · title | match (exact_title_primary_author), target at #3 | OL267171W War and Peace [A]; OL267096W Анна Каренина [D]; OL45017724W War and Peace [A] |
| ho-289 | guerra del fin del mundo | match/ambiguous · La guerra del fin del mundo | none (weak_evidence) |  |
| ho-296 | Dombey and Sons | match/ambiguous · Dombey and Son | ambiguous (weak_evidence), target at #2 | OL81295W Sons and Lovers [E]; OL14868861W Dombey and Son [E]; OL43370W Отцы и дети [E] |
| ho-298 | rider grey | match/ambiguous · The Mysterious Rider | ambiguous (near_title_author), target at #2 | OL485504W Riders of the Purple Sage [C]; OL485697W The Mysterious Rider [C]; OL485423W The Desert of Wheat [D] |
| ho-299 | songs of a sourdoe robert service | match/ambiguous · Songs of a sourdough | ambiguous (author_only), target at #5 | OL24034W Treasure Island [D]; OL24156W The Strange Case of Dr. Jekyll and Mr. … [D]; OL2010879W Rich Dad, Poor Dad [D] |
| ho-304 | la nouvel eloiz russo | match/ambiguous · La nouvelle Héloïse | ambiguous (author_only) | OL3351636W Empire Falls [D]; OL19870W The Jungle Book [E]; OL17357733W If I Was Your Girl [D] |
| ho-311 | saunders of river | match/ambiguous · Sanders of the river | ambiguous (author_only) | OL20938W The Mill on the Floss [D]; OL21026339W Rivers [A]; OL17635834W Lincoln in the Bardo [D] |
| ho-313 | Utopia for Realistic People | match/ambiguous · Utopia for Realists | ambiguous (weak_evidence), target at #5 | OL15366912W Utopia [E]; OL1063267W How to Win Friends and Influence People [E]; OL2629977W The 7 Habits of Highly Effective People [E] |
| ho-315 | Mortal Frends Jane Stanton Hitchcok | match/ambiguous · Mortal Friends | ambiguous (author_only) | OL66554W Pride and Prejudice [D]; OL66513W Emma [D]; OL66562W Sense and Sensibility [D] |
| ho-318 | The Bishop mystery | match/ambiguous · The Bishop murder case | none (weak_evidence) |  |
| ho-322 | war which saved my life | match/ambiguous · The war that Saved my Life | none (weak_evidence) |  |
| ho-326 | master and margarita bulgakov | match/ambiguous · title | match (exact_title_primary_author), target at #2 | OL676009W Мастер и Маргарита [A]; OL40220376W Master I Margarita [C]; OL45435048W Master i Margarita [C] |
| ho-335 | zarathustra book philosophy | match/ambiguous · Also sprach Zarathustra | none (weak_evidence) |  |
| ho-345 | dune anderson | match/ambiguous · Sandworms of Dune | match (exact_title_primary_author), target at #3 | OL27776707W Dune [A]; OL19618275W Dune [A]; OL14961045W Sandworms of Dune [C] |
| ho-353 | The Black Flame Weinbowm | match/ambiguous · The black flame | none (weak_evidence) |  |
| ho-356 | Rebels with a Caws Patterson | match/ambiguous · Rebels with a Cause | ambiguous (author_only) | OL167189W 1st to Die [D]; OL167178W Along Came a Spider [D]; OL167185W Jack & Jill [D] |
| ho-357 | arow of god achebbe | match/ambiguous · Arrow of God | none (weak_evidence) |  |
| ho-362 | oktaedro julio cortasar | match/ambiguous · Octaedro | ambiguous (author_only) | OL14860424W Rayuela [D]; OL276963W Final del juego [D]; OL8971528W Todos los fuegos el fuego [D] |
| ho-363 | The Heavenly Servant Bunyan | match/ambiguous · Heavenly footman | ambiguous (author_only), target at #2 | OL107195W The Pilgrim's Progress [D]; OL107193W Heavenly footman [D]; OL107194W Grace abounding to the chief of sinners [D] |
| ho-365 | daughter grows up in marshalsea prison with her father | match/ambiguous · Little Dorrit | none (weak_evidence) |  |
| ho-367 | white worm illustrated stoker | match/ambiguous · Lair of the White Worm Illustrated | ambiguous (author_only) | OL85892W Dracula [D]; OL85875W Bram Stoker's Dracula omnibus [D]; OL85891W The Jewel of Seven Stars [D] |
| ho-370 | book of five circles musashi | match/ambiguous · Gorin no sho | ambiguous (near_title_author), target at #2 | OL39568417W The Book of Five Rings [C]; OL3946622W Gorin no sho [D]; OL15118455W Musashi's Book of Five Rings [C] |
| ho-375 | Obscure Fates Cather | match/ambiguous · Obscure Destinies | ambiguous (author_only), target at #5 | OL12791W My Ántonia [D]; OL12795W O Pioneers! [D]; OL12832W Death Comes for the Archbishop [D] |
| ho-376 | economics book cheating teachers sumo wrestlers | match/ambiguous · Freakonomics | ambiguous (weak_evidence), target at #5 | OL1972993W Economics [E]; OL1648145W Économie internationale [E]; OL14996318W Economics [E] |
| ho-380 | wheel of time book one | match/ambiguous · title | ambiguous (weak_evidence) | OL52267W The Time Machine [E]; OL8193387W Hard Times [E]; OL44986W Jude the Obscure [E] |
| ho-382 | Sykee Erwin Rode | match/ambiguous · Psyche | ambiguous (author_only) | OL3246392W The Audacity of Hope [D]; OL1210514W What is life? The physical aspect of th… [D]; OL25821897W Toy Box [D] |
| ho-385 | girl follows magic thread to rescue miner boy | match/ambiguous · The Princess and the Goblin | ambiguous (weak_evidence) | OL45887W Boy [E]; OL453657W The Colour of Magic [E]; OL5840870W The Boy in the Striped Pyjamas [E] |
| ho-390 | Anne Beddingfeld mystery adventure | match/ambiguous · The Man in the Brown Suit | none (weak_evidence) |  |
| ho-404 | burnett mary lennox | match/ambiguous · The Secret Garden | ambiguous (author_only), target at #2 | OL450063W Frankenstein; or, The Modern Prometheus [D]; OL69612W The Secret Garden [D]; OL69630W A Little Princess [D] |
| ho-405 | two children recovering after a sledding accident | match/ambiguous · Jack and Jill | none (weak_evidence) |  |
| ho-406 | Racine Nero | match/ambiguous · Britannicus | ambiguous (author_only), target at #4 | OL19922727W Phèdre [D]; OL503424W Quo Vadis? Powieść z czasów Nerona [E]; OL741414W Théâtre complet [D] |
| ho-407 | story about a dog in belgium | match/ambiguous · A Dog of Flanders | none (no_candidates) |  |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
