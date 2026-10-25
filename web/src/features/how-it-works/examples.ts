/*
 * The worked examples on the How it works page. Every value is real output of the API's C# pipeline for these inputs
 * (QueryCleaner, HypothesisSanitizer, RetrievalLadder, MatchRanker, GroundingCheck, TemplateExplainer) on the recorded
 * Open Library responses in tests/fixtures. If that code changes, re-run it and update these; never round by hand.
 */

// ---------------------------------------------------------------- 1. cleaning

/** A piece of the folded text; `was` is what the user typed there when folding changed it ("" was added: none). */
export interface FoldPiece {
  text: string
  was?: string
}

export type TokenKind = "search" | "edition" | "small" | "year" | "number"

export interface CleanToken {
  text: string
  kind: TokenKind
}

export interface CleanExample {
  id: string
  label: string
  typed: string
  folded: FoldPiece[]
  foldNote: string
  tokens: CleanToken[]
  /** Number words joined into one search token ("nineteen eighty four" → 1984). */
  joined?: string
  sent: string
  keptAside: string[]
  takeaway: string
}

export const TOKEN_LABEL: Record<TokenKind, string> = {
  search: "searched",
  edition: "edition word",
  small: "small word",
  year: "year",
  number: "number word",
}

export const CLEAN_EXAMPLES: CleanExample[] = [
  {
    id: "noisy",
    label: "Noisy",
    typed: "tolkien hobbit illustrated deluxe 1937",
    folded: [{ text: "tolkien hobbit illustrated deluxe 1937" }],
    foldNote: "Already plain lowercase text, so nothing changes.",
    tokens: [
      { text: "tolkien", kind: "search" },
      { text: "hobbit", kind: "search" },
      { text: "illustrated", kind: "edition" },
      { text: "deluxe", kind: "edition" },
      { text: "1937", kind: "year" },
    ],
    sent: "q=tolkien hobbit",
    keptAside: [
      "1937: a small bonus for books first published that year, never a filter",
      "illustrated, deluxe: they describe an edition, not which book",
    ],
    takeaway:
      "Edition words would make Open Library hunt for those exact words. Setting them aside keeps the search on the book itself.",
  },
  {
    id: "symbols",
    label: "Symbols",
    typed: "Pride & Prejudice",
    folded: [
      { text: "p", was: "P" },
      { text: "ride " },
      { text: "and", was: "&" },
      { text: " " },
      { text: "p", was: "P" },
      { text: "rejudice" },
    ],
    foldNote: "Lowercase, and & becomes the word “and”.",
    tokens: [
      { text: "pride", kind: "search" },
      { text: "and", kind: "small" },
      { text: "prejudice", kind: "search" },
    ],
    sent: "q=pride prejudice",
    keptAside: ["and: too common to search for, but it still counts when a title contains it"],
    takeaway: "“Pride & Prejudice” and “pride and prejudice” become the same search.",
  },
  {
    id: "accents",
    label: "Accents",
    typed: "Les Misérables",
    folded: [
      { text: "l", was: "L" },
      { text: "es " },
      { text: "m", was: "M" },
      { text: "is" },
      { text: "e", was: "é" },
      { text: "rables" },
    ],
    foldNote: "Accents are folded the way Open Library folds them, so é and e match.",
    tokens: [
      { text: "les", kind: "search" },
      { text: "miserables", kind: "search" },
    ],
    sent: "q=les miserables",
    keptAside: [],
    takeaway: "Typing with or without the accent finds the same book.",
  },
  {
    id: "numbers",
    label: "Number words",
    typed: "nineteen eighty-four",
    folded: [{ text: "nineteen eighty-four" }],
    foldNote: "Already lowercase. The hyphen splits the words in the next step.",
    tokens: [
      { text: "nineteen", kind: "number" },
      { text: "eighty", kind: "number" },
      { text: "four", kind: "number" },
    ],
    joined: "1984",
    sent: "q=1984",
    keptAside: [],
    takeaway: "Catalog titles get the same treatment, so “Nineteen Eighty-Four” and “1984” compare as equal.",
  },
  {
    id: "scripts",
    label: "Other scripts",
    typed: "三体",
    folded: [{ text: "三体" }],
    foldNote: "Letters of every script are kept exactly as typed: 三体, Анна Каренина and 채식주의자 stay whole.",
    tokens: [{ text: "三体", kind: "search" }],
    sent: "title=三体",
    keptAside: [],
    takeaway:
      "Open Library refuses q= searches shorter than 3 characters, so a short query goes as a title search instead.",
  },
  {
    id: "elision",
    label: "French article",
    typed: "l'étranger camus",
    folded: [{ text: "", was: "l'" }, { text: "e", was: "é" }, { text: "tranger camus" }],
    foldNote: "é becomes e, and the French article l’ is dropped, as the catalog does.",
    tokens: [
      { text: "etranger", kind: "search" },
      { text: "camus", kind: "search" },
    ],
    sent: "q=etranger camus",
    keptAside: [],
    takeaway: "“l'étranger”, “L’Étranger” and “l etranger” all become the same search.",
  },
]

// ---------------------------------------------------------------- 2. the AI's reading

export interface ReadField {
  name: string
  value: string
  kept: boolean
  why: string
}

export interface ReadExample {
  id: string
  label: string
  typed: string
  fields: ReadField[]
  /** The Open Library requests the reading leads to, besides the broad search. */
  requests: string[]
  outcome: string
}

export const READ_EXAMPLES: ReadExample[] = [
  {
    id: "characters",
    label: "Character names",
    typed: "ignatius reilly",
    fields: [
      { name: "title", value: "A Confederacy of Dunces", kept: true, why: "Backed by a character name you typed" },
      { name: "author", value: "John Kennedy Toole", kept: true, why: "You typed no author, so the AI’s can count" },
      { name: "characters", value: "Ignatius Reilly", kept: true, why: "You typed it" },
    ],
    requests: ["q=ignatius reilly", "title=confederacy dunces&author=toole"],
    outcome: "The AI knew the book behind the name. Code adds one focused search for it.",
  },
  {
    id: "noisy",
    label: "Noisy",
    typed: "tolkien hobbit illustrated deluxe 1937",
    fields: [
      { name: "title", value: "The Hobbit, or There and Back Again", kept: true, why: "“hobbit” was typed" },
      { name: "author", value: "J.R.R. Tolkien", kept: true, why: "“tolkien” was typed" },
      {
        name: "characters",
        value: "Bilbo, Gandalf, Thorin",
        kept: false,
        why: "None was typed: the model added them from memory",
      },
      {
        name: "keywords",
        value: "fantasy, adventure",
        kept: false,
        why: "Not typed, so they can’t be shown as your clues",
      },
    ],
    requests: ["q=tolkien hobbit"],
    outcome: "The AI adds no word you didn’t type, so no extra search is needed.",
  },
  {
    id: "slip",
    label: "A real slip",
    typed: "mark huckleberry",
    fields: [
      {
        name: "title",
        value: "Mark Twain",
        kept: false,
        why: "Same as the author: the model copied the name into the title",
      },
      { name: "author", value: "Mark Twain", kept: true, why: "“mark” was typed" },
      { name: "characters", value: "Huckleberry Finn", kept: true, why: "“huckleberry” was typed" },
    ],
    requests: ["q=mark huckleberry", "author=twain"],
    outcome: "The bad title never reaches Open Library. The author still narrows the search.",
  },
  {
    id: "topic",
    label: "A topic",
    typed: "find me a book about bears",
    fields: [
      { name: "intent", value: "topic", kept: true, why: "“a book about” was typed" },
      { name: "subjects", value: "bears", kept: true, why: "A subject heading, as a library catalog writes it" },
      {
        name: "suggestions",
        value: "The Jungle Book, Winnie-the-Pooh",
        kept: true,
        why: "Each is shown only if Open Library has it",
      },
    ],
    requests: ["q=bears", "q=subject:(bears)", "q=title:(jungle book) OR title:(winnie pooh)"],
    outcome: "A topic gets several fitting books and is never called one match.",
  },
]

// ---------------------------------------------------------------- 3. searching Open Library

export type RungId = "broad" | "reading" | "anyOf" | "characters" | "authorWorks"

export const RUNGS: { id: RungId; name: string; when: string }[] = [
  { id: "broad", name: "Broad search", when: "Always, at the same moment the AI starts reading" },
  { id: "reading", name: "The AI’s title and author", when: "When the AI adds a word you didn’t type" },
  { id: "anyOf", name: "Any of the words", when: "When the evidence is weak: rescues typos" },
  { id: "characters", name: "Character names", when: "When no title matches: your words may be characters" },
  { id: "authorWorks", name: "The author’s best-known works", when: "When you typed only an author" },
]

export interface LadderCall {
  rung: RungId
  request: string
  results: number
  /** What the ranking shows after this call, and why the ladder stops or climbs. */
  after: string
  stop: boolean
}

export interface LadderExample {
  id: string
  typed: string
  calls: LadderCall[]
}

export const LADDER_EXAMPLES: LadderExample[] = [
  {
    id: "hobbit",
    typed: "tolkien hobbit illustrated deluxe 1937",
    calls: [
      {
        rung: "broad",
        request: "q=tolkien hobbit",
        results: 20,
        after: "The Hobbit is an exact title by the typed author: strong evidence.",
        stop: true,
      },
    ],
  },
  {
    id: "dickens",
    typed: "dickens",
    calls: [
      {
        rung: "broad",
        request: "q=dickens",
        results: 20,
        after: "Only an author was typed, so show their best-known books.",
        stop: false,
      },
      {
        rung: "authorWorks",
        request: "author_key=OL24638A&sort=editions",
        results: 10,
        after: "A Christmas Carol leads Charles Dickens’s most-published works.",
        stop: true,
      },
    ],
  },
  {
    id: "scout",
    typed: "scout atticus",
    calls: [
      {
        rung: "broad",
        request: "q=scout atticus",
        results: 7,
        after: "No title matches both words: weak evidence.",
        stop: false,
      },
      {
        rung: "anyOf",
        request: "q=scout OR atticus",
        results: 20,
        after: "Still no title match. The words may be character names.",
        stop: false,
      },
      {
        rung: "characters",
        request: "q=person:(scout AND atticus)",
        results: 6,
        after: "To Kill a Mockingbird lists Scout Finch and Atticus Finch as characters.",
        stop: true,
      },
    ],
  },
]

// ---------------------------------------------------------------- 4. matching and ranking

export interface TypoPair {
  typed: string
  title: string
  same: boolean
  why: string
}

export const TYPO_PAIRS: TypoPair[] = [
  { typed: "hobit", title: "hobbit", same: true, why: "One letter missing" },
  { typed: "tolkein", title: "tolkien", same: true, why: "Two swapped letters count as one slip" },
  { typed: "prince", title: "princess", same: false, why: "One word starts the other: a different word, not a typo" },
  { typed: "1984", title: "1985", same: false, why: "Numbers must match exactly" },
  { typed: "it", title: "is", same: false, why: "Words under 4 letters must match exactly" },
]

export type Tier = "A" | "B" | "C" | "D" | "E"

export const TIERS: { tier: Tier; rule: string; example: string }[] = [
  { tier: "A", rule: "Exact title, by the primary author", example: "tolkien hobbit finds The Hobbit" },
  {
    tier: "B",
    rule: "Exact title, the name is a contributor",
    example: "hobbit dixon finds the graphic novel Dixon adapted",
  },
  { tier: "C", rule: "Close title, plus an author", example: "mark huckleberry, with the AI off" },
  { tier: "D", rule: "Only an author", example: "dickens shows his most-published books" },
  { tier: "E", rule: "Some words or characters, nothing clear", example: "scout atticus, with the AI off" },
]

export type ScoreTerm = "title" | "author" | "editions" | "readers" | "year" | "characters" | "penalty"

export const SCORE_TERMS: { term: ScoreTerm; name: string; rule: string }[] = [
  { term: "title", name: "Title", rule: "4 × how well the title matches (1 when exact)" },
  { term: "author", name: "Author", rule: "2 for a primary author, 1 for a contributor" },
  { term: "editions", name: "Editions", rule: "2.5 × log₁₀ of the edition count" },
  { term: "readers", name: "Readers", rule: "0.5 × log₁₀ of Open Library reading logs" },
  { term: "year", name: "Year", rule: "0.5 when it was first published within a year of yours" },
  { term: "characters", name: "Characters", rule: "4 × the share of your words that are its characters" },
  {
    term: "penalty",
    name: "Penalties",
    rule: "minus 3 when you named someone not on the book; minus 1.5 to 2.5 for study guides, box sets and adaptations nobody asked for",
  },
]

export interface ScoredBook {
  title: string
  detail: string
  tier: Tier
  terms: Partial<Record<ScoreTerm, number>>
  total: number
  /** Added for ordering only: tier B gets +2 so a matched contributor beats the famous original. */
  orderBonus?: number
}

export interface RankExample {
  id: string
  typed: string
  books: [ScoredBook, ScoredBook]
  verdict: string
}

export const RANK_EXAMPLES: RankExample[] = [
  {
    id: "hobbit",
    typed: "tolkien hobbit illustrated deluxe 1937",
    books: [
      {
        title: "The Hobbit",
        detail: "J.R.R. Tolkien, 482 editions, 1937",
        tier: "A",
        terms: { title: 4, author: 2, editions: 6.71, readers: 1.86, year: 0.5 },
        total: 15.07,
      },
      {
        title: "The Fellowship of the Ring",
        detail: "J.R.R. Tolkien, 319 editions",
        tier: "D",
        terms: { author: 2, editions: 6.26, readers: 1.7 },
        total: 9.96,
      },
    ],
    verdict: "The Hobbit wins on every count, and its exact title by the typed author puts it in tier A.",
  },
  {
    id: "dixon",
    typed: "hobbit dixon",
    books: [
      {
        title: "The Hobbit (graphic novel)",
        detail: "J.R.R. Tolkien; Charles Dixon, adapter; 10 editions",
        tier: "B",
        terms: { title: 4, author: 1, editions: 2.6, readers: 1.09 },
        total: 8.69,
        orderBonus: 2,
      },
      {
        title: "The Hobbit",
        detail: "J.R.R. Tolkien, 482 editions",
        tier: "E",
        terms: { title: 3, editions: 6.71, readers: 1.86, penalty: -3 },
        total: 8.57,
      },
    ],
    verdict:
      "The famous original has 48 times the editions, but you named Dixon, who isn’t on it. The adaptation wins.",
  },
  {
    id: "scout",
    typed: "scout atticus",
    books: [
      {
        title: "To Kill a Mockingbird",
        detail: "Harper Lee, 213 editions",
        tier: "E",
        terms: { editions: 5.83, readers: 1.91, characters: 4 },
        total: 11.73,
      },
      {
        title: "Go Set a Watchman",
        detail: "Harper Lee, 32 editions",
        tier: "E",
        terms: { editions: 3.8, readers: 1.1, characters: 4 },
        total: 8.9,
      },
    ],
    verdict:
      "Both list Scout and Atticus as characters; popularity breaks the tie. Without the AI no title was typed, so it stays tier E.",
  },
]

export type Outcome = "match" | "ambiguous" | "none"

export interface DecisionExample {
  id: string
  typed: string
  tier: Tier
  /** How far the checks get: 0 tier, 1 safety rules, 2 lead. The failing check, or 3 when all pass. */
  reached: 0 | 1 | 2 | 3
  outcome: Outcome
  note: string
}

export const DECISION_EXAMPLES: DecisionExample[] = [
  {
    id: "hobbit",
    typed: "tolkien hobbit illustrated deluxe 1937",
    tier: "A",
    reached: 3,
    outcome: "match",
    note: "Tier A, no rule fires, and no other tier A book is close.",
  },
  {
    id: "dixon",
    typed: "hobbit dixon",
    tier: "B",
    reached: 3,
    outcome: "match",
    note: "Tier B counts as a match too: the exact title, with Dixon credited as adapter.",
  },
  {
    id: "dickens",
    typed: "dickens",
    tier: "D",
    reached: 0,
    outcome: "ambiguous",
    note: "Only an author was typed. The hint says: “These are Charles Dickens's most-published works. Add a title to narrow it.”",
  },
  {
    id: "potter",
    typed: "harry potter",
    tier: "E",
    reached: 0,
    outcome: "ambiguous",
    note: "Every book in the series starts with those words, and none is titled just “Harry Potter”.",
  },
]

// ---------------------------------------------------------------- 5. explanations

export const HOBBIT_FACTS: { field: string; value: string }[] = [
  { field: "title", value: "The Hobbit" },
  { field: "match_phrase", value: "Exact title match" },
  { field: "primary_authors", value: "J.R.R. Tolkien" },
  { field: "first_publish_year", value: "1937" },
  { field: "edition_count", value: "482" },
]

export const TEMPLATE_SENTENCE =
  "Exact title match; J.R.R. Tolkien is the primary author; Open Library's first publication year 1937 matches the year in the search; 1 duplicate record merged."

export interface SentenceExample {
  id: string
  label: string
  sentence: string
  /** Words (lowercase, without punctuation) and characters the check rejected. Empty: the sentence passed. */
  flagged: string[]
  problems: string[]
}

export const SENTENCE_EXAMPLES: SentenceExample[] = [
  {
    id: "grounded",
    label: "Grounded",
    sentence:
      "Exact title match; J.R.R. Tolkien is the primary author; Open Library's first publication year 1937 matches the year in the search; 482 editions.",
    flagged: [],
    problems: [],
  },
  {
    id: "praise",
    label: "Invented praise",
    sentence: "Exact title match; a beloved fantasy classic by J.R.R. Tolkien!",
    flagged: ["beloved", "fantasy", "classic", "!"],
    problems: [
      "“beloved”, “fantasy” and “classic” are not in the facts",
      "“!” is not allowed: one calm statement only",
    ],
  },
  {
    id: "injected",
    label: "Injected text",
    sentence: "Exact title match; J.R.R. Tolkien is the primary author; free download at hobbitpdf dot ru.",
    flagged: ["free", "download", "hobbitpdf", "dot", "ru"],
    problems: ["Five words are not in the facts or the short list of explanation words"],
  },
  {
    id: "number",
    label: "Wrong number",
    sentence: "Exact title match; J.R.R. Tolkien is the primary author; 500 editions.",
    flagged: ["500"],
    problems: ["500 is not a number in the facts (it has 482 editions)"],
  },
  {
    id: "kind",
    label: "Wrong match",
    sentence: "Near title match; J.R.R. Tolkien is the primary author.",
    flagged: ["near"],
    problems: ["Code found an exact match; “near” contradicts it and “exact” is missing"],
  },
]
