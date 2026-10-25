# Eval: Find That Book HELD-OUT set v1 · final3-heldout-live-openai-hyp-judge

> **HELD-OUT RESULTS.** Sealed set, final evaluation only. Nothing may be tuned on these rows.

> HELD-OUT RESULTS: sealed set, final evaluation only. Do not tune anything on these rows.

| Run | |
|---|---|
| Date | 2026-10-25 03:56 MT (-06:00) · 2026-10-25 05:56 ET (-04:00) |
| Code fingerprint (src/) | `sha256:918d7b88cc5867cf (151 files)` |
| Pipeline binaries (Core + Infrastructure dlls loaded) | `sha256:bb794a4ff02178f3 (2 files)` |
| Eval tool fingerprint | `sha256:bfa0664db82124b7 (31 files)` |
| Dataset | Find That Book HELD-OUT set v1 (`eval/heldout.json`, sha256:b2570dcf3f70231a) |
| Held out | **yes** |
| Tuning | HELD-OUT (sealed): never tuned on; final evaluation only |
| Catalog | live Open Library (https://openlibrary.org, the adapter's own throttle ≤ 3 req/s, cache as configured) |
| Provider (requested) | none |
| Models (observed in traces) | interpret: replay/openai/gpt-4.1-mini<br>judge: gpt-6-luna |
| Interpretations | replayed from `find-that-book-repo/eval/results/2026-10-25-final3-heldout-live-openai-interpret/raw/results.jsonl` (408 entries; 0 hypothesis_miss rows ran LLM-off); **interpretations recorded on build `sha256:918d7b88cc5867cf (151 files)`** (interpret: openai/gpt-4.1-mini), ranking and status re-run on build `sha256:918d7b88cc5867cf (151 files)` |
| Explanations | template only (--explain template: the explain LLM is never called) |
| Ranker | `default` |
| Judge | on |
| Caches | as configured (cold at process start) |
| Counts | 409 cases · 409 run · 0 skipped · 0 rejected by validation · 1 errors · 0 in-sample |
| Concurrency / limit | 2 / 5 |
| Wall time | 358.4 s |
| Command | `dotnet run --project tools/FindThatBook.Eval -- run --set heldout --heldout --catalog live --provider none --hypotheses eval/results/2026-10-25-final3-heldout-live-openai-interpret/raw/results.jsonl --explain template --judge on --max-llm-calls 450 --quiet --label final3-heldout-live-openai-hyp-judge` |
| Per-row results | `raw/results.jsonl` (409 rows; gitignored, kept locally) |

## Headline

| Metric | Value | Definition |
|---|---|---|
| **Match precision** | **272/282 = .965** | `match` rows whose top-1 is right (and the row allows match) |
| Top-1 | 333/406 = .820 | answerable rows, first shown candidate acceptable |
| Top-5 | 359/406 = .884 | answerable rows, an acceptable candidate in the first five |
| Status accuracy | 381/409 = .932 | outcome in the row's accepted statuses (incl. `rejected` = validation 400) |
| Coverage (auto-accept) | 282/406 = .695 | answerable rows returned as `match` |
| False matches on no-answer rows | 0 of 3 | any `match` where no book is right |
| Severe matches | 10 | wrong or false `match` (battle severe; a duplicate record of the right book is not counted) |
| Duplicate-record matches | 0 | `match` on another record of the intended book (battle major) |
| Errors | 1 | the search threw (catalog failure, timeout, crash) |

## By category

| Category | n | top-1 | top-5 | status | `match` @ precision | false match |
|---|---|---|---|---|---|---|
| hand | 52 | 45/49 = .918 | 48/49 = .980 | 50/52 = .962 | 36 @ .944 | 0 |
| llm_author_character | 8 | 7/8 = .875 | 8/8 = 1.000 | 8/8 = 1.000 | 3 @ 1.000 | 0 |
| llm_half_remembered | 54 | 37/54 = .685 | 42/54 = .778 | 47/54 = .870 | 31 @ .903 | 0 |
| llm_misspeller | 61 | 47/61 = .770 | 48/61 = .787 | 55/61 = .902 | 41 @ .976 | 0 |
| llm_non_native | 52 | 47/52 = .904 | 50/52 = .962 | 50/52 = .962 | 45 @ .956 | 0 |
| llm_plot | 23 | 7/23 = .304 | 10/23 = .435 | 14/23 = .609 | 1 @ .000 | 0 |
| llm_rushed_mobile | 80 | 74/80 = .925 | 77/80 = .963 | 79/80 = .988 | 69 @ .986 | 0 |
| prog_drop | 8 | 4/8 = .500 | 8/8 = 1.000 | 8/8 = 1.000 | 0 @ n/a | 0 |
| prog_format | 16 | 16/16 = 1.000 | 16/16 = 1.000 | 16/16 = 1.000 | 13 @ 1.000 | 0 |
| prog_swap | 18 | 17/18 = .944 | 18/18 = 1.000 | 18/18 = 1.000 | 14 @ 1.000 | 0 |
| prog_typo | 22 | 17/22 = .773 | 19/22 = .864 | 21/22 = .955 | 15 @ 1.000 | 0 |
| prog_year | 15 | 15/15 = 1.000 | 15/15 = 1.000 | 15/15 = 1.000 | 14 @ 1.000 | 0 |

## Judge

- Verdicts: agreed 322, chose_none 2, disagreed 13, none 1, skipped 71
- Agreement with code's #1: 322/337 = .955 (agreed / agreed + disagreed + chose_none)
- On 335 judged rows with a pick and known ids: judge pick right 317, code #1 right 314
- Second-opinion slots shown: 13
- Judge latency p50 / p95: 157 ms / 304 ms

## Explanations

- Source mix (shown candidates): llm 0, template 1889 (.000 llm)
- Grounding rejections (template shown although the explain call succeeded): 0
- Rows with explanation_fallback (explain call failed/unavailable): 389

## Degraded

- Degraded rate: 389/408 = .953
  - of which 387 only because of this run's settings (LLM off / --explain template / hypothesis replay: ai_not_configured, explanation_fallback)
- Rows with a provider error (timeout / 5xx / 429 on interpret or explain; not our budget): 1/408 = .002
- interpretation_fallback: 1
- explanation_fallback: 389
- catalog_partial: 1
- Interpretation source: llm 407, heuristic 1

## Latency

| Measure | n | p50 | p95 | max |
|---|---|---|---|---|
| Wall per query (validate + scope + search) | 409 | 1127 ms | 5327 ms | 16392 ms |
| Pipeline elapsed_ms | 408 | 1125 ms | 5163 ms | 16392 ms |
| stage `clean` | 408 | 0 ms | 0 ms | 4 ms |
| stage `explain` | 389 | 0 ms | 3 ms | 21 ms |
| stage `interpret` | 408 | 0 ms | 0 ms | 5 ms |
| stage `judge` | 389 | 153 ms | 284 ms | 799 ms |
| stage `rank` | 408 | 12 ms | 48 ms | 119 ms |
| stage `retrieve` | 408 | 942 ms | 5108 ms | 16391 ms |
| Open Library call (not cache hits) | 829 | 579 ms | 2680 ms | 15009 ms |
| LLM `explain` (not cache hits) | 0 | n/a | n/a | n/a |
| LLM `interpret` (not cache hits) | 0 | n/a | n/a | n/a |

## Open Library calls

- Logical calls per query: mean 2.12, max 5, at budget 21 (budget: 5 logical calls)
- Cache hits: 37/866 = .043
- HTTP attempts: 830 total, mean 2.03 per query; slow-empty re-queries 0
- Outcomes: ok 865, failed 1
- Steps: broad 408, hypothesis 223, any_of 105, original_title 44, person_fallback 31, person_fallback_any 25, person 19, author_works 7, person_any 4

## LLM usage and cost

| Operation | calls | cache hits | statuses | input tok | output tok |
|---|---|---|---|---|---|
| explain | 389 | 0 | not_configured 389 | n/a | n/a |
| interpret | 408 | 408 | ok 407, timeout 1 | n/a | n/a |
| judge | 337 | n/a | see Judge | 247000 | n/a |

- Paid LLM calls (not cache hits, incl. judge): 337
- Estimated cost (IAiPricing, paid-tier list prices): $0.0247 for 408 searches = **$0.060539 per 1,000 searches**

## Misses (73)

| Row | Query | Expected | Got | Top-3 shown |
|---|---|---|---|---|
| ho-019 | Un London mieville | match/ambiguous · Un Lun Dun | ambiguous (author_only), target at #4 | OL14942956W The Call of the Wild [D]; OL74502W The Iron Heel [D]; OL74504W White Fang [D] |
| ho-020 | mane traveled rodes | match/ambiguous · Main-travelled roads | none (weak_evidence) |  |
| ho-023 | Shakespeare's Tragedies Bradley | match/ambiguous · Shakespearean Tragedy | ambiguous (author_only) | OL9170454W Hamlet [D]; OL258902W Macbeth [D]; OL258850W Othello [D] |
| ho-036 | the federalist hamiltno | match/ambiguous · The Federalist, or, The New Constitution | ambiguous (judge_low_confidence) | OL27105877W Federalist Papers [A]; OL26972971W Federalist Papers [A]; OL27156299W Federalist Papers Annotated [C] |
| ho-040 | captain bonaville travel | match/ambiguous · The adventures of Captain Bonneville, U.S.A. | none (weak_evidence) |  |
| ho-047 | wolfvilee nights leewis | match/ambiguous · Wolfville nights | none (weak_evidence) |  |
| ho-056 | Little girl big DeWoskin | match/ambiguous · Big girl small | none (weak_evidence) |  |
| ho-058 | two dogs and a cat finding their way home | match/ambiguous · The Incredible Journey | none (weak_evidence) |  |
| ho-059 | passing third floor back | match/ambiguous · The passing of the third floor back | ambiguous (judge_disagreed), target at #2 | OL2645476W Passing [A]; OL243550W The passing of the third floor back [A]; OL2645474W Quicksand ; and, Passing [C] |
| ho-065 | bulldog drummond at bay mnceile | match/ambiguous · Bulldog Drummond at bay | ambiguous (judge_disagreed), target at #2 | OL241909W Bulldog Drummond [C]; OL92777W Bulldog Drummond at bay [A]; OL92767W Bulldog Drummond's Third Round [C] |
| ho-071 | 1764 essay against torture and capital punishment | match/ambiguous · Dei delitte e delle pene | none (weak_evidence) |  |
| ho-082 | Posies Lawrence poems | match/ambiguous · Pansies | match (judge_agreed) | OL7965798W The complete poems of D.H. Lawrence [A]; OL31512215W The Complete Poems of D. H. Lawrence in… [C]; OL35303355W The Complete Poems of D. H. Lawrence, 2… [C] |
| ho-085 | Sweep jonathan oxier | match/ambiguous · Sweep | ambiguous (author_only) | OL20600W Gulliver's Travels [D]; OL4082696W The Corrections [D]; OL262438W The Sign of Four [E] |
| ho-092 | paetrson williiams | match/ambiguous · Paterson | ambiguous (author_only) | OL274433W The Classic Tale of the Velveteen Rabbit [D]; OL30294W A Streetcar Named Desire [D]; OL30293W The Glass Menagerie [D] |
| ho-095 | niven the ringworld engineers | match/ambiguous · The Ringworld Engineers (Ringworld) | ambiguous (author_only), target at #2 | OL510405W Ringworld [D]; OL510404W The Ringworld Engineers (Ringworld) [A]; OL510428W The Ringworld throne [D] |
| ho-098 | fantastico george macdonal | match/ambiguous · Phantastes | ambiguous (author_only) | OL1168083W Nineteen Eighty-Four [D]; OL1168007W Animal Farm [D]; OL15449W The Princess and the Goblin [D] |
| ho-101 | The Hearts Invisible Furees | match/ambiguous · The Heart's Invisible Furies | ambiguous (weak_evidence), target at #2 | OL52266W The Invisible Man [E]; OL19551794W The Heart's Invisible Furies [A]; OL38663W Heart of Darkness [E] |
| ho-124 | Bulldog Drummond in bay | match/ambiguous · Bulldog Drummond at bay | ambiguous (judge_disagreed), target at #2 | OL241909W Bulldog Drummond [C]; OL92777W Bulldog Drummond at bay [A]; OL92767W Bulldog Drummond's Third Round [C] |
| ho-128 | Czech drama about manufactured workers rebelling against humans | match/ambiguous · R.U.R. and The insect play | ambiguous (author_only) | OL2239396W À rebours [D]; OL505781W Of Human Bondage [E]; OL362691W Much Ado About Nothing [E] |
| ho-134 | La petite princesse | match/ambiguous · A Little Princess | match (judge_agreed) | OL8019128W La petite princesse [A]; OL25433W Princess and the Pea [E]; OL24233579W La petite princesse [A] |
| ho-138 | sailor memoir aboard brig Pilgrim | match/ambiguous · Two years before the mast | none (weak_evidence) |  |
| ho-143 | black amazon mars | match/ambiguous · Black Amazon of Mars | ambiguous (author_only) | OL53908W Adventures of Huckleberry Finn [D]; OL28577W The Souls of Black Folk [E]; OL15854658W Black Beauty [E] |
| ho-152 | vieu souvenirs du prince de joinvil | match/ambiguous · Vieux souvenirs de Mgr le prince de Joinville, 1818-1848 | error (catalog_timeout) |  |
| ho-154 | England and the English from an Amerikan point of view | match/ambiguous · England and the English from an American point of view | none (weak_evidence) |  |
| ho-158 | future society novel responding to Orwell | match/ambiguous · 1985 | ambiguous (author_only) | OL1168083W Nineteen Eighty-Four [D]; OL1168007W Animal Farm [D]; OL1168169W Homage to Catalonia [D] |
| ho-160 | metal man searches for his lost sweetheart | match/ambiguous · The Tin Woodman of Oz | ambiguous (judge_chose_none) | OL5711572W Metal Man [A]; OL40334767W Metal Man [A]; OL29022019W Metal Man [A] |
| ho-165 | death row prisoner healing powers pet mouse | match/ambiguous · The Green Mile | none (weak_evidence) |  |
| ho-168 | Russian writer nonfiction account of Soviet prison camps | match/ambiguous · Архипелаг ГУЛАГ | none (weak_evidence) |  |
| ho-172 | Le phénomène humain | match/ambiguous · The Phenomenon of Man | match (judge_agreed), target at #2 | OL459264W Le phénomène humain [A]; OL459280W The Phenomenon of Man [E]; OL33965773W Le phénomène humain [A] |
| ho-175 | Half Servings Ferber | match/ambiguous · Half Portions | ambiguous (author_only), target at #2 | OL2646769W So Big [D]; OL2646760W Half Portions [D]; OL82565W Harry Potter and the Half-Blood Prince [E] |
| ho-182 | free | match/ambiguous · The Free Fishers | ambiguous (weak_evidence), target at #3 | OL51142W Free air [E]; OL52257W The World Set Free [E]; OL76573W The Free Fishers [E] |
| ho-183 | portions | match/ambiguous · Half Portions | ambiguous (weak_evidence), target at #4 | OL4304829W Food values of portions commonly used [E]; OL507381W The portion of labor [E]; OL1535782W Memoirs of John Quincy Adams [E] |
| ho-186 | jane eyre wide sargasso | match/ambiguous · title | ambiguous (author_only) | OL66554W Pride and Prejudice [D]; OL66513W Emma [D]; OL66562W Sense and Sensibility [D] |
| ho-189 | sord of flame maggie fury | match/ambiguous · Sword of Flame | ambiguous (weak_evidence) | OL20839W Maggie, a girl of the streets [E]; OL82870W The Sound and the Fury [E]; OL17860744W A Court of Mist and Fury [E] |
| ho-190 | Portraits of Fidelman | match/ambiguous · Pictures of Fidelman | none (weak_evidence) |  |
| ho-195 | history england letters | match/ambiguous · A history of England in a series of letters from a nobleman to his son | ambiguous (author_only) | OL769120W Book of common prayer [D]; OL9250413W A Child's History of England [E]; OL93567W The history of England [E] |
| ho-196 | Critical works of James Joyce | match/ambiguous · The critical writings of James Joyce | ambiguous (author_only) | OL86320W Dubliners [D]; OL43024W As a Man Thinketh [D]; OL77958W The Last of the Mohicans [D] |
| ho-202 | The Mystery Rider Zane Grey | match/ambiguous · The Mysterious Rider | ambiguous (author_only), target at #2 | OL485504W Riders of the Purple Sage [D]; OL485697W The Mysterious Rider [D]; OL17448W King Solomon's Mines [D] |
| ho-205 | achilles anger during siege of troy | match/ambiguous · Ἰλιάς | match (judge_agreed) | OL40715510W Iliad [A]; OL35743338W The Iliad [B]; OL40678138W Iliad of Homer [C] |
| ho-217 | chief legaty | match/ambiguous · The Chief Legatee | match (judge_agreed) | OL5966054W Legacy of the chief [A]; OL5866653W The judicial legacy of Chief Justice Ro… [E]; OL28941859W Proceedings of a Conference on Early Mo… [E] |
| ho-221 | The Beest Must Dye | match/ambiguous · The Beast Must Die | none (weak_evidence) |  |
| ho-227 | Rewind Creech | match/ambiguous · Replay | ambiguous (author_only) | OL14937643W Walk Two Moons [D]; OL31545W Love That Dog [D]; OL31551W The Wanderer [D] |
| ho-232 | Portrait of Chekhov | match/ambiguous · The image of Chekhov | ambiguous (judge_disagreed) | OL55496W Note-Book of Anton Chekhov [C]; OL55472W The Stories of Anton Chekhov [23 storie… [C]; OL55518W The portable Chekhov [D] |
| ho-244 | biographical essays about Plato Swedenborg Montaigne Napoleon | match/ambiguous · Representative men | ambiguous (author_only) | OL1086528W Essais [D]; OL51831W πολιτεία [D]; OL527464W Think and Grow Rich [D] |
| ho-249 | kig rat meville | match/ambiguous · King Rat | ambiguous (judge_disagreed), target at #2 | OL2918736W King Rat [A]; OL14871882W King Rat [A]; OL463327W Rat race [E] |
| ho-255 | Engineers of Ringworld Larry Niven | match/ambiguous · The Ringworld Engineers (Ringworld) | ambiguous (author_only), target at #2 | OL510405W Ringworld [D]; OL510404W The Ringworld Engineers (Ringworld) [A]; OL510428W The Ringworld throne [D] |
| ho-260 | story about vincent van gogh | match/ambiguous · Lust for Life | ambiguous (author_only), target at #2 | OL518262W Vincent van Gogh [D]; OL112003W Lust for Life [E]; OL518259W The letters of Vincent van Gogh [D] |
| ho-268 | The Demon Doctor Rohmer | match/ambiguous · The Devil Doctor | ambiguous (author_only) | OL2288684W The Insidious Dr. Fu Manchu [D]; OL2288676W Brood of the Witch-Queen [D]; OL2288675W The Hand of Fu-Manchu [D] |
| ho-271 | dei delitti e delle peni beccaria | match/ambiguous · Dei delitte e delle pene | match (judge_agreed) | OL43798230W Dei delitti e delle pene [A]; OL44735138W Dei delitti e delle pene del marchese C… [C]; OL43798202W Dei delitti e delle pene di Cesare Becc… [C] |
| ho-273 | A fewg in time | match/ambiguous · A fugue in time | ambiguous (weak_evidence) | OL52267W The Time Machine [E]; OL8193387W Hard Times [E]; OL44986W Jude the Obscure [E] |
| ho-285 | The Empty House and Other Tales | match/ambiguous · The Empty House and Other Ghost Stories | none (weak_evidence) |  |
| ho-287 | war and peace tolstoy | match/ambiguous · title | match (judge_agreed), target at #3 | OL267171W War and Peace [A]; OL267096W Анна Каренина [D]; OL45017724W War and Peace [A] |
| ho-298 | rider grey | match/ambiguous · The Mysterious Rider | ambiguous (near_title_author), target at #2 | OL485504W Riders of the Purple Sage [C]; OL485697W The Mysterious Rider [C]; OL485423W The Desert of Wheat [D] |
| ho-305 | Liao jai jee yee Pu Sung ling | match/ambiguous · 聊齋誌異 | ambiguous (unsupported_guess) | OL11543852W Strange stories from a Chinese studio [A]; OL43981488W 聊斋志异 [A]; OL28711054W Foxes in Strange Stories from a Chinese… [C] |
| ho-306 | wrinkle in time graphic novel | match/ambiguous · title | ambiguous (weak_evidence), target at #2 | OL17192857W Madeline L'Engle's A Wrinkle in Time-Th… [E]; OL19360587W A Wrinkle In Time [E]; OL41495W A Wrinkle in Time [A] |
| ho-311 | saunders of river | match/ambiguous · Sanders of the river | ambiguous (author_only) | OL20938W The Mill on the Floss [D]; OL21026339W Rivers [A]; OL17635834W Lincoln in the Bardo [D] |
| ho-313 | Utopia for Realistic People | match/ambiguous · Utopia for Realists | ambiguous (weak_evidence), target at #5 | OL15366912W Utopia [E]; OL1063267W How to Win Friends and Influence People [E]; OL2629977W The 7 Habits of Highly Effective People [E] |
| ho-315 | Mortal Frends Jane Stanton Hitchcok | match/ambiguous · Mortal Friends | ambiguous (author_only) | OL66554W Pride and Prejudice [D]; OL66513W Emma [D]; OL66562W Sense and Sensibility [D] |
| ho-318 | The Bishop mystery | match/ambiguous · The Bishop murder case | none (weak_evidence) |  |
| ho-320 | sandworms dune | match/ambiguous · Sandworms of Dune | match (judge_agreed) | OL893414W Dune [A]; OL893461W Dune Messiah [C]; OL893516W Children of Dune [C] |
| ho-326 | master and margarita bulgakov | match/ambiguous · title | match (judge_agreed), target at #2 | OL676009W Мастер и Маргарита [A]; OL40220376W Master I Margarita [C]; OL45435048W Master i Margarita [C] |
| ho-333 | paradiso | match/ambiguous · Paradiso | ambiguous (near_title_author), target at #2 | OL93230W Il Paradiso [C]; OL44237703W Paradiso [A]; OL810991W Paradise Lost [E] |
| ho-345 | dune anderson | match/ambiguous · Sandworms of Dune | ambiguous (judge_low_confidence), target at #3 | OL27776707W Dune [A]; OL19618275W Dune [A]; OL14961045W Sandworms of Dune [C] |
| ho-367 | white worm illustrated stoker | match/ambiguous · Lair of the White Worm Illustrated | ambiguous (judge_disagreed), target at #3 | OL85875W Bram Stoker's Dracula omnibus [C]; OL29827658W Lair of the White Worm [A]; OL26825776W Lair of the White Worm Illustrated [C] |
| ho-370 | book of five circles musashi | match/ambiguous · Gorin no sho | match (judge_agreed) | OL39568417W The Book of Five Rings [A]; OL37791428W A Book of Five Rings [A]; OL38366815W Book of Five Rings and Dokkodo [C] |
| ho-375 | Obscure Fates Cather | match/ambiguous · Obscure Destinies | ambiguous (author_only), target at #5 | OL12791W My Ántonia [D]; OL12795W O Pioneers! [D]; OL12832W Death Comes for the Archbishop [D] |
| ho-376 | economics book cheating teachers sumo wrestlers | match/ambiguous · Freakonomics | ambiguous (weak_evidence), target at #5 | OL1972993W Economics [E]; OL1648145W Économie internationale [E]; OL14996318W Economics [E] |
| ho-382 | Sykee Erwin Rode | match/ambiguous · Psyche | ambiguous (author_only) | OL3246392W The Audacity of Hope [D]; OL1210514W What is life? The physical aspect of th… [D]; OL25821897W Toy Box [D] |
| ho-385 | girl follows magic thread to rescue miner boy | match/ambiguous · The Princess and the Goblin | none (weak_evidence) |  |
| ho-390 | Anne Beddingfeld mystery adventure | match/ambiguous · The Man in the Brown Suit | none (weak_evidence) |  |
| ho-405 | two children recovering after a sledding accident | match/ambiguous · Jack and Jill | none (weak_evidence) |  |
| ho-406 | Racine Nero | match/ambiguous · Britannicus | ambiguous (weak_evidence), target at #5 | OL503424W Quo Vadis? Powieść z czasów Nerona [E]; OL19922727W Phèdre [D]; OL4242235W History of Nero [E] |
| ho-407 | story about a dog in belgium | match/ambiguous · A Dog of Flanders | ambiguous (weak_evidence), target at #4 | OL17610986W Dog Man [E]; OL5682519W Dog Days [E]; OL2965102W The Curious Incident of the Dog in the … [E] |

Parse stability and retrieval variability are measured by the `stability` command (separate report).
