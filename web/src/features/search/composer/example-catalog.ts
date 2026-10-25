/**
 * Queries the search is built to answer, grouped the way a person would try them.
 * `needsAi` ones need Gemini, OpenAI or Anthropic. Everything else works with the AI off.
 * Choosing one fills the composer; it never searches by itself.
 */
export type SearchExample = {
  /** Exactly what is put in the search box. */
  query: string
  needsAi?: boolean
}

export type SearchExampleGroup = {
  title: string
  /** One sentence under the title, when the title doesn't already say what happens. */
  note?: string
  queries: readonly SearchExample[]
}

export const SEARCH_EXAMPLE_GROUPS: readonly SearchExampleGroup[] = [
  {
    title: "Just a title",
    queries: [
      { query: "the hobbit" },
      { query: "frankenstein" },
      { query: "the great gatsby" },
      { query: "moby dick" },
      { query: "to kill a mockingbird" },
    ],
  },
  {
    title: "Title and author, in either order",
    queries: [
      { query: "dracula stoker" },
      { query: "1984 orwell" },
      { query: "huxley brave new world" },
      { query: "of mice and men steinbeck" },
      { query: "dickens tale two cities" },
    ],
  },
  {
    title: "Just an author",
    note: "Returns their most-published books.",
    queries: [
      { query: "dickens" },
      { query: "jane austen" },
      { query: "mark twain" },
      { query: "stephen king" },
      { query: "j.k. rowling" },
    ],
  },
  {
    title: "Messy queries with edition words, formats and years",
    queries: [
      { query: "tolkien hobbit illustrated deluxe 1937" },
      { query: "the great gatsby fitzgerald penguin classics 2003" },
      { query: "pride and prejudice hardcover annotated 1813" },
      { query: "moby dick melville paperback unabridged" },
      { query: "The Adventures of Huckleberry Finn by Mark Twain (Illustrated) [Annotated]" },
    ],
  },
  {
    title: "Typos",
    queries: [
      { query: "tolkein hobbit" },
      { query: "great expecttions dikens" },
      { query: "harry poter philosophers stone" },
      { query: "farenheit 451 bradbury" },
      { query: "wuthering hieghts" },
    ],
  },
  {
    title: "Partial titles with words missing",
    queries: [
      { query: "anne green gables" },
      { query: "old man sea hemingway" },
      { query: "kill mockingbird" },
      { query: "tale two cities" },
    ],
  },
  {
    title: "Full titles, subtitles and other countries' titles",
    note: "Harry Potter and the Sorcerer's Stone is the US title.",
    queries: [
      { query: "the hobbit or there and back again" },
      { query: "frankenstein or the modern prometheus" },
      { query: "harry potter and the sorcerer's stone" },
    ],
  },
  {
    title: "Accents and symbols, typed or not",
    note: "Les Misérables matches with or without the accents.",
    queries: [
      { query: "les misérables hugo" },
      { query: "les miserables hugo" },
      { query: "garcía márquez one hundred years of solitude" },
      { query: "bronte jane eyre" },
      { query: "pride & prejudice" },
    ],
  },
  {
    title: "Characters",
    note: "With the AI on, the marked names come back as a confident match.",
    queries: [
      { query: "scout atticus" },
      { query: "ahab whale" },
      { query: "bilbo dragon" },
      { query: "sherlock baskerville" },
      { query: "frodo ring" },
      { query: "dorian gray" },
      { query: "holden caulfield", needsAi: true },
      { query: "gatsby daisy", needsAi: true },
      { query: "elizabeth darcy", needsAi: true },
    ],
  },
  {
    title: "An author's first name or surname plus a clue",
    queries: [{ query: "mark huckleberry" }, { query: "twilight meyer" }, { query: "austen bennet" }],
  },
  {
    title: "Illustrators, adapters and special editions",
    note: "For “hobbit dixon”, Tolkien stays the author and Dixon is credited as the adapter.",
    queries: [
      { query: "hobbit dixon" },
      { query: "hobbit graphic novel" },
      { query: "the hobbit tolkien dixon" },
      { query: "sparknotes catcher in the rye" },
    ],
  },
  {
    title: "Other languages and scripts",
    note: "“the three body problem” finds the Chinese original, 三体, known in English as The Three-Body Problem.",
    queries: [
      { query: "le petit prince" },
      { query: "Анна Каренина" },
      { query: "ノルウェイの森" },
      { query: "三体" },
      { query: "the three body problem" },
    ],
  },
  {
    title: "Very short queries",
    note: "“it” is Stephen King's It. Abbreviations need the AI.",
    queries: [{ query: "it" }, { query: "1984" }, { query: "lotr", needsAi: true }],
  },
  {
    title: "Natural sentences and topics",
    note: "Returns several books on the topic.",
    queries: [
      { query: "find me a book about bears" },
      { query: "books about dragons" },
      { query: "a novel set in paris" },
      { query: "novels about world war 2" },
      { query: "something about dinosaurs" },
      { query: "picture books about trains" },
      { query: "books about the ocean for kids" },
      { query: "stories about pirates" },
      { query: "books about foxes" },
      { query: "books about bears and wolves" },
      { query: "i want to read about the french revolution" },
      { query: "books like harry potter", needsAi: true },
      { query: "un livre sur les ours", needsAi: true },
      { query: "teach me about volcanoes", needsAi: true },
    ],
  },
  {
    title: "Describing the plot",
    queries: [{ query: "the book where a pig and a spider are friends", needsAi: true }],
  },
  {
    title: "Request phrasing around a title",
    note: "The extra words are ignored.",
    queries: [
      { query: "find me the hobbit" },
      { query: "can you find pride and prejudice" },
      { query: "i'm looking for a book called the giver" },
    ],
  },
  {
    title: "Real titles that contain words like “about” or “find me”",
    note: "They still find the book.",
    queries: [
      { query: "about a boy" },
      { query: "much ado about nothing" },
      { query: "we need to talk about kevin" },
      { query: "looking for alaska" },
      { query: "find me aciman" },
      { query: "the book thief" },
      { query: "something wicked this way comes" },
      { query: "on the road kerouac" },
    ],
  },
  {
    title: "Vague queries",
    note: "You get choices, not one guess.",
    queries: [{ query: "twilight" }, { query: "emma" }, { query: "meyer" }, { query: "hermione" }],
  },
  {
    title: "Searches that correctly find nothing",
    note: "You get a hint instead of a guessed book.",
    queries: [
      { query: "xqzv plorft wibble" },
      { query: "the purple elephant who sang opera on mars" },
      { query: "recommend me a good book" },
    ],
  },
]

/**
 * Topic requests that still miss with the AI on. Shown as a limitation, never as an example to try.
 * (Venice has no single Open Library heading; the cookbook is treated as a title; Lincoln's books
 * come back without subject headings the page can show.)
 */
export const STILL_MISSED_TOPICS = [
  "a mystery set in venice",
  "a cookbook for italian food",
  "a biography of abraham lincoln",
] as const
