import { fireEvent, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it } from "vitest"
import type { SearchResponse } from "@/api/types"
import { toSnapshot } from "@/features/history/snapshot"
import { historyStore, HISTORY_STORAGE_KEY, HISTORY_VERSION } from "@/features/history/storage"
import { deferred, fakeApi, json, problem, type Handler } from "@/test/fake-api"
import { fixtures, searchExamples } from "@/test/fixtures"
import { renderApp } from "@/test/render"

/** Search is the paid call, so every test owns its handler. Suggestions and Admin status are free and stubbed. */
function api(search: Handler) {
  return fakeApi({
    "GET /api/books/search": search,
    "GET /api/suggest": () => json(fixtures.suggestEmpty()),
    "GET /api/admin/ai": () => problem({ status: 503 }),
    "GET /api/health": () => json(fixtures.health()),
  })
}

async function search(text = "tolkien hobbit") {
  const user = userEvent.setup()
  const app = await renderApp("/")
  await user.type(screen.getByLabelText("Describe the book"), `${text}{Enter}`)
  return { user, ...app }
}

const HEADINGS = { match: "Best match", ambiguous: "A few books fit your clues", none: "No book matched" } as const

/** The heading a response gets: topic requests ("a book about bears") are named by their topics. */
function headingFor(response: SearchResponse): string {
  const topics =
    response.interpretation.intent === "topic" ? new Intl.ListFormat().format(response.interpretation.topics) : null
  if (!topics) return HEADINGS[response.status]
  return response.status === "none" ? `No books found about ${topics}` : `Books about ${topics}`
}

/** A topic answer built from the ambiguous example, the way the API shapes one ("find me a book about bears"). */
function topicAnswer(): SearchResponse {
  const response = fixtures.ambiguous()
  response.status_reason = "topic_search"
  response.hint = "Books about bears. Add a title or author to find a specific one."
  response.interpretation = {
    ...response.interpretation,
    intent: "topic",
    topics: ["bears"],
    title: null,
    author: null,
  }
  response.candidates = response.candidates.map((c, i) => ({
    ...c,
    match_tier: "e",
    matched_subjects: i === 0 ? ["Bears -- Fiction", "Juvenile fiction"] : [],
  }))
  return response
}
const STEP_TITLES = [
  "Understanding your clues",
  "Searching Open Library",
  "Comparing matches",
  "Writing explanations",
  "Second opinion",
]

beforeEach(() => historyStore.resetForTests())

describe("topic requests", () => {
  it("lists books about the topic as equals, never one best match, with the subjects that matched", async () => {
    const response = topicAnswer()
    api(() => json(response))
    await search("find me a book about bears")

    expect(await screen.findByRole("heading", { level: 2, name: "Books about bears" })).toBeVisible()
    expect(screen.queryByRole("heading", { name: "Best match" })).not.toBeInTheDocument()
    expect(screen.getByText(response.hint!)).toBeVisible()
    const subjects = screen.getByRole("list", { name: "Matching Open Library subjects" })
    expect(
      within(subjects)
        .getAllByRole("listitem")
        .map((li) => li.textContent)
    ).toEqual(["Bears -- Fiction", "Juvenile fiction"])
    expect(screen.getAllByRole("list", { name: "Matching Open Library subjects" })).toHaveLength(1)
  })

  it("names the topic when nothing was found", async () => {
    const response = topicAnswer()
    response.status = "none"
    response.candidates = []
    response.hint = "Add a topic, a title or an author."
    api(() => json(response))
    await search("find me a book about bears")
    expect(await screen.findByRole("heading", { level: 2, name: "No books found about bears" })).toBeVisible()
    expect(screen.getByText("Add a topic, a title or an author.")).toBeVisible()
  })
})

describe("results for every API example", () => {
  it.each(searchExamples())(
    "$name: status heading, every candidate in server order with its required fields",
    async ({ response }) => {
      api(() => json(response))
      await search()

      expect(await screen.findByRole("heading", { level: 2, name: headingFor(response) })).toBeVisible()
      for (const other of Object.values(HEADINGS).filter((h) => h !== headingFor(response))) {
        expect(screen.queryByRole("heading", { name: other })).not.toBeInTheDocument()
      }

      const items = screen.queryAllByRole("listitem").filter((li) => within(li).queryByRole("heading", { level: 3 }))
      expect(items.map((li) => within(li).getByRole("heading", { level: 3 }).textContent)).toEqual(
        response.candidates.map((c) => c.title)
      )
      response.candidates.forEach((c, i) => {
        const li = within(items[i])
        expect(li.getByText(c.explanation)).toBeVisible()
        if (c.author) expect(li.getByText(c.author)).toBeVisible()
        else expect(li.getByText("Author not listed")).toBeVisible()
        if (c.first_publish_year !== null)
          expect(li.getByText(new RegExp(`First published ${c.first_publish_year}`))).toBeVisible()
        const about = [c.author, c.first_publish_year].filter((part) => part !== null).join(", ")
        const link = li.getByRole("link", {
          name: about ? `Open “${c.title}” (${about}) on Open Library` : `Open “${c.title}” on Open Library`,
        })
        expect(link).toHaveAttribute("href", c.open_library_url)
        expect(link).toHaveAttribute("target", "_blank")
        expect(link).toHaveAttribute("rel", "noopener noreferrer")
        // Covers are decorative (the title is the h3 beside them), so they are found by element, not by role.
        const cover = items[i].querySelector("img")
        if (c.cover_url) expect(cover).toHaveAttribute("src", c.cover_url)
        if (c.cover_url) expect(cover).toHaveAttribute("alt", "")
      })
      expect(screen.getByText(/Results for/)).toHaveTextContent("Results for “tolkien hobbit”")
    }
  )

  it.each(searchExamples())("$name: activity lists only the stages the response timed", async ({ response }) => {
    api(() => json(response))
    const { user } = await search()
    await user.click(await screen.findByRole("button", { name: /Search activity/ }))

    const reported = new Set(Object.keys(response.meta.timings_ms))
    const judged = !["off", "skipped"].includes(response.meta.judge.status)
    const expected = {
      "Understanding your clues": reported.has("interpret"),
      "Searching Open Library": reported.has("retrieve"),
      "Comparing matches": reported.has("rank"),
      "Writing explanations": reported.has("explain"),
      "Second opinion": judged,
    }
    for (const [title, shown] of Object.entries(expected)) {
      if (shown) expect(screen.getByText(title)).toBeVisible()
      else expect(screen.queryByText(title)).not.toBeInTheDocument()
    }
    expect(screen.getByText(`Request ID: ${response.meta.request_id}`)).toBeInTheDocument()
  })
})

describe("candidate details", () => {
  it("keeps primary authors and contributors apart, contributors as quieter credits", async () => {
    const response = fixtures.contributorMatch()
    Object.assign(response.candidates[0], {
      author: "J.R.R. Tolkien",
      contributors: [
        { key: null, name: "Charles Dixon", role: "adapter" },
        { key: null, name: "Sean Deming", role: "adapter" },
        { key: null, name: "Langston Hughes", role: "also credited" },
      ],
      contributors_total: 3,
    })
    api(() => json(response))
    await search("hobbit dixon")
    const best = (await screen.findByRole("heading", { name: "Best match" })).closest("section")!
    expect(within(best).getByText("J.R.R. Tolkien").parentElement).toHaveTextContent(/^by J\.R\.R\. Tolkien$/)
    expect(within(best).getByText(/^Also credited:/)).toHaveTextContent(
      /^Also credited: Charles Dixon \(adapter\), Sean Deming \(adapter\), Langston Hughes$/
    )
    expect(within(best).queryByText(/^by .*Dixon/)).not.toBeInTheDocument()
    expect(screen.getByRole("heading", { level: 2, name: "Other possibilities" })).toBeVisible()
  })

  it("says how many more contributors there are, and shows no year when Open Library has none", async () => {
    const response = fixtures.contributorMatch()
    const first = response.candidates[0]
    first.contributors_total = first.contributors.length + 4
    first.first_publish_year = null
    first.edition_count = 4
    api(() => json(response))
    await search()
    const best = (await screen.findByRole("heading", { name: "Best match" })).closest("section")!
    expect(within(best).getByText(/^Also credited:/)).toHaveTextContent(/ and 4 more$/)
    expect(within(best).queryByText(/First published/)).not.toBeInTheDocument()
    expect(within(best).getByText("4 editions")).toBeVisible()
  })

  it("marks the second-opinion pick in words, not only on hover", async () => {
    const response = fixtures.ambiguous()
    response.candidates[1].second_opinion = true
    api(() => json(response))
    await search("dickens")
    const items = await screen.findAllByRole("listitem")
    const flagged = items.find((li) => within(li).queryByText("Second opinion"))!
    expect(within(flagged).getByRole("heading", { level: 3 })).toHaveTextContent(response.candidates[1].title)
    expect(within(flagged).getByText(/A second AI check picked this book/)).toBeInTheDocument()
    expect(screen.getAllByText("Second opinion")).toHaveLength(1)
  })

  it("never puts a non-http URL into an href or src", async () => {
    const response = fixtures.ambiguous()
    response.candidates[0].open_library_url = "javascript:alert(1)"
    response.candidates[0].cover_url = "javascript:alert(2)"
    response.candidates[1].cover_url = "data:image/png;base64,AAAA"
    api(() => json(response))
    const { container } = await search("dickens")
    await screen.findByRole("heading", { name: HEADINGS.ambiguous })

    expect(container.querySelector('[href^="javascript:"], [src^="javascript:"], [src^="data:"]')).toBeNull()
    const [first, second] = screen.getAllByRole("listitem")
    expect(within(first).queryByRole("link")).not.toBeInTheDocument()
    expect(first.querySelector("img")).toBeNull()
    expect(second.querySelector("img")).toBeNull()
  })

  it("names Open Library links apart when results share a title, with whatever author and year exist", async () => {
    const response = fixtures.ambiguous()
    const [a, b, c] = response.candidates
    Object.assign(a, { title: "Collected Stories", author: "Anton Chekhov", first_publish_year: 1900 })
    Object.assign(b, { title: "Collected Stories", author: null, first_publish_year: 1951 })
    Object.assign(c, { title: "Collected Stories", author: null, first_publish_year: null })
    api(() => json(response))
    await search("collected stories")
    await screen.findByRole("heading", { name: HEADINGS.ambiguous })

    for (const name of [
      "Open “Collected Stories” (Anton Chekhov, 1900) on Open Library",
      "Open “Collected Stories” (1951) on Open Library",
      "Open “Collected Stories” on Open Library",
    ]) {
      expect(screen.getByRole("link", { name })).toBeVisible()
    }
  })

  it("swaps a cover that fails to load for the paper placeholder, keeping the title", async () => {
    const response = fixtures.match()
    const { title } = response.candidates[0]
    api(() => json(response))
    await search()
    const best = (await screen.findByRole("heading", { name: "Best match" })).closest("section")!
    fireEvent.error(best.querySelector("img")!)
    expect(best.querySelector("img")).toBeNull()
    expect(within(best).getByText(title.match(/[\p{L}\p{N}]/u)![0].toLocaleUpperCase())).toBeInTheDocument()
    expect(within(best).getByRole("heading", { level: 3, name: title })).toBeVisible()
  })
})

describe("degraded answers", () => {
  it("explains a rate-limited AI quietly and keeps the results", async () => {
    const response = fixtures.degraded()
    response.degraded_reasons = ["interpretation_fallback", "explanation_fallback"]
    response.interpretation.source = "heuristic"
    for (const step of response.meta.llm.steps) step.status = "rate_limited"
    api(() => json(response))
    const { user } = await search("mark huckleberry")

    const notice = await screen.findByRole("note")
    expect(notice).toHaveTextContent(
      "AI help wasn't available for this search, so it used the words as typed and standard explanations."
    )
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Try again" })).not.toBeInTheDocument()
    expect(screen.getByRole("heading", { level: 3, name: response.candidates[0].title })).toBeVisible()

    await user.click(screen.getByRole("button", { name: /Search activity/ }))
    expect(screen.getByText("AI was busy, so it used the words as typed.")).toBeVisible()
    expect(screen.getByText(`Searched for: ${response.interpretation.keywords.join(", ")}`)).toBeVisible()
  })

  it("shows the API's degraded example as a note, not an error", async () => {
    const response = fixtures.degraded()
    api(() => json(response))
    await search("mark huckleberry")
    expect(await screen.findByRole("note")).toBeVisible()
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
    expect(screen.getAllByRole("heading", { level: 3 })).toHaveLength(response.candidates.length)
  })

  it.each([
    {
      reasons: ["ai_not_configured", "explanation_fallback"] as const,
      text: "AI isn't set up for this app, so this search used the words as typed and standard explanations.",
    },
    {
      reasons: ["ai_budget_exhausted", "interpretation_fallback", "explanation_fallback"] as const,
      text: "The AI usage limit was reached, so this search used the words as typed and standard explanations.",
    },
    {
      reasons: ["explanation_fallback"] as const,
      text: "AI help wasn't available for this search, so the explanations use standard wording.",
    },
  ])("words $reasons plainly", async ({ reasons, text }) => {
    const response = fixtures.degraded()
    response.degraded_reasons = [...reasons]
    api(() => json(response))
    await search("mark huckleberry")
    expect(await screen.findByRole("note")).toHaveTextContent(text)
  })

  it("doesn't invent a cause when the deadline skipped the AI work", async () => {
    const response = fixtures.degraded()
    response.degraded_reasons = ["deadline_reached"]
    response.interpretation.source = "heuristic"
    response.meta.llm.steps = []
    response.candidates.forEach((c) => (c.explanation_source = "template"))
    api(() => json(response))
    const { user } = await search("mark huckleberry")

    expect(await screen.findByRole("note")).toHaveTextContent(
      "The search reached its time limit, so it shows what it found in time."
    )
    await user.click(screen.getByRole("button", { name: /Search activity/ }))
    expect(screen.getByText("Used the words as typed.")).toBeVisible()
    expect(screen.getByText("Used standard wording.")).toBeVisible()
    expect(screen.queryByText(/isn't set up|usage limit|was busy/)).not.toBeInTheDocument()
  })

  it("warns when some books may be missing", async () => {
    const response = fixtures.match()
    response.degraded = true
    response.degraded_reasons = ["catalog_partial"]
    api(() => json(response))
    await search()
    expect(await screen.findByRole("note")).toHaveTextContent(
      "Open Library didn't answer every search, so some books may be missing."
    )
    expect(screen.getByRole("heading", { name: "Best match" })).toBeVisible()
  })
})

describe("failures are not empty results", () => {
  const cases: { name: string; handler: Handler; title: string }[] = [
    {
      name: "502 catalog_bad_response",
      handler: () => problem(fixtures.catalogBadResponse()),
      title: "Open Library sent an unusable answer",
    },
    {
      name: "503 catalog_unavailable",
      handler: () => problem(fixtures.catalogUnavailable()),
      title: "Open Library is unavailable",
    },
    {
      name: "504 catalog_timeout",
      handler: () => problem(fixtures.catalogTimeout()),
      title: "Open Library didn't answer in time",
    },
    {
      name: "504 search_timeout",
      handler: () => problem(fixtures.searchTimeout()),
      title: "The search didn't finish in time",
    },
    {
      name: "network",
      handler: () => {
        throw new TypeError("Failed to fetch")
      },
      title: "Couldn't reach the search service",
    },
  ]

  it.each(cases)("$name: an inline error with Try again, never “No book matched”", async ({ handler, title }) => {
    const calls = api(handler)
    const { user } = await search()
    expect(await screen.findByText(title)).toBeVisible()
    expect(screen.queryByText("No book matched")).not.toBeInTheDocument()
    expect(screen.queryByRole("heading", { level: 2 })).not.toBeInTheDocument()
    expect(screen.getByLabelText("Describe the book")).toHaveValue("tolkien hobbit")
    expect(calls.count("GET", "/api/books/search")).toBe(1)

    await user.click(screen.getByRole("button", { name: "Try again" }))
    await waitFor(() => expect(calls.count("GET", "/api/books/search")).toBe(2))
  })

  it("keeps keyboard focus on Stop when Try again or Search again starts the search", async () => {
    const pending = deferred()
    const calls = api((url, init) =>
      calls.count("GET", "/api/books/search") === 1
        ? problem(fixtures.catalogBadResponse())
        : pending.handler(url, init)
    )
    const { user } = await search()

    // The failed search: Try again unmounts as the new search starts.
    ;(await screen.findByRole("button", { name: "Try again" })).focus()
    await user.keyboard("{Enter}")
    await waitFor(() => expect(screen.getByRole("button", { name: "Stop" })).toHaveFocus())

    // The stopped search: Search again unmounts the same way.
    await user.keyboard("{Enter}")
    screen.getByRole("button", { name: "Search again" }).focus()
    await user.keyboard("{Enter}")
    await waitFor(() => expect(screen.getByRole("button", { name: "Stop" })).toHaveFocus())
    expect(calls.count("GET", "/api/books/search")).toBe(3)
  })

  it("shows the reference for a server bug", async () => {
    api(() => problem({ status: 500, code: "internal_error", title: "Internal error", trace_id: "00-abc-01" } as never))
    await search()
    expect(await screen.findByText("Something went wrong on the server")).toBeVisible()
    expect(screen.getByText("00-abc-01")).toBeVisible()
  })
})

describe("running and stored searches", () => {
  it("shows only an honest running state while the search is in flight", async () => {
    const pending = deferred()
    api(pending.handler)
    await search()
    expect(await screen.findByText("Searching…", { selector: "[data-slot=trace-title]" })).toBeVisible()
    expect(screen.queryByRole("button", { name: /Search activity/ })).not.toBeInTheDocument()
    for (const title of STEP_TITLES) expect(screen.queryByText(title)).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Searching…"))

    pending.resolve(json(fixtures.match()))
    expect(await screen.findByRole("heading", { name: "Best match" })).toBeVisible()
    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("Best match: The Hobbit by J.R.R. Tolkien")
    )
  })

  it("restores a saved answer without calling the API, and searches again only when asked", async () => {
    const entry = {
      id: "saved0000001",
      query: "dickens",
      savedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
      result: toSnapshot(fixtures.ambiguous()),
    }
    window.localStorage.setItem(HISTORY_STORAGE_KEY, JSON.stringify({ version: HISTORY_VERSION, entries: [entry] }))
    historyStore.resetForTests()
    const calls = api(() => json(fixtures.ambiguous()))
    const user = userEvent.setup()
    await renderApp(`/?entry=${entry.id}`)

    expect(await screen.findByRole("heading", { name: HEADINGS.ambiguous })).toBeVisible()
    expect(screen.getByText(/Results for/)).toHaveTextContent("Results for “dickens”")
    expect(screen.getByText(/^Saved/)).toHaveTextContent("Saved 5 minutes ago")
    expect(screen.queryByRole("button", { name: /Search activity/ })).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Showing a saved search"))
    expect(calls.count("GET", "/api/books/search")).toBe(0)

    await user.click(screen.getByRole("button", { name: "Search again" }))
    await waitFor(() => expect(calls.count("GET", "/api/books/search")).toBe(1))
  })

  it("explains a saved search this browser doesn't have", async () => {
    const calls = api(() => json(fixtures.match()))
    const user = userEvent.setup()
    const { router } = await renderApp("/?entry=missing00001")
    expect(
      await screen.findByRole("heading", { level: 2, name: "This saved search isn't on this device" })
    ).toBeVisible()
    await user.click(screen.getByRole("link", { name: "Back to search" }))
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty("entry"))
    expect(screen.getByRole("heading", { level: 1, name: "Which book are you thinking of?" })).toBeVisible()
    expect(calls.count("GET", "/api/books/search")).toBe(0)
  })

  it("offers to edit the search when nothing matched", async () => {
    const response: SearchResponse = fixtures.none()
    api(() => json(response))
    const { user } = await search("xqzv blorptangle")
    expect(await screen.findByText(response.hint!)).toBeVisible()
    await user.click(screen.getByRole("button", { name: "Edit your search" }))
    await waitFor(() => expect(screen.getByLabelText("Describe the book")).toHaveFocus())
  })
})
