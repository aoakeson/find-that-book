import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"
import type { SearchResponse } from "@/api/types"
import { toSnapshot } from "@/features/history/snapshot"
import { historyStore } from "@/features/history/storage"
import { fakeApi, json, manual, problem, type Handler } from "@/test/fake-api"
import { adminStatus, fixtures } from "@/test/fixtures"
import { renderApp } from "@/test/render"
import type { SearchSession } from "./session"

/*
 * The search state machine, exercised inside the real app (router, providers, SearchPage's ?entry= sync). The session
 * is read through a spy on useSearchSession, so these tests don't depend on how the results UI draws each state.
 */
const probe = vi.hoisted(() => ({ current: null as SearchSession | null }))

vi.mock("./session", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./session")>()
  function useProbedSearchSession() {
    const session = actual.useSearchSession()
    probe.current = session
    return session
  }
  return { ...actual, useSearchSession: useProbedSearchSession }
})

function session(): SearchSession {
  if (!probe.current) throw new Error("Nothing has rendered the search session yet")
  return probe.current
}
const view = () => session().view

/** What the composer does: the text goes into the draft, then the draft is submitted. */
function typeAndSubmit(text: string) {
  act(() => session().setDraft(text))
  act(() => session().submit())
}

/** Answers with an API example, echoing the searched text as `query` like the API does. */
const answer =
  (make: () => SearchResponse): Handler =>
  (url) =>
    json({ ...make(), query: url.searchParams.get("q") ?? "" })

const savedQueries = () => historyStore.getSnapshot().entries.map((e) => e.query)
const searchCount = (api: ReturnType<typeof fakeApi>) => api.count("GET", "/api/books/search")
/** Suggestions are free and may be fetched while typing; they never take part in these flows. */
const quietSuggest = { "GET /api/suggest": () => json(fixtures.suggestEmpty()) }

beforeEach(() => {
  probe.current = null
})

describe("a search", () => {
  it("saves the answer to history and pushes ?entry=<id>", async () => {
    const api = fakeApi({ "GET /api/books/search": answer(fixtures.match) })
    const { router, history } = await renderApp("/")

    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view().kind).toBe("success"))

    const [entry] = historyStore.getSnapshot().entries
    expect(savedQueries()).toEqual(["tolkien hobbit"])
    expect(entry.result).toMatchObject({ query: "tolkien hobbit", status: "match" })
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: entry.id }))
    // The URL change doesn't turn the live answer into a restore, and nothing is fetched again.
    expect(view()).toMatchObject({ kind: "success", entryId: entry.id })
    expect(searchCount(api)).toBe(1)

    // A push, not a replace: Back returns to the bare search page.
    act(() => history.back())
    await waitFor(() => expect(router.state.location.search).toEqual({}))
    await waitFor(() => expect(view().kind).toBe("home"))
    expect(searchCount(api)).toBe(1)
  })

  it("sends exactly one request for one Enter inside <StrictMode>", async () => {
    const api = fakeApi({ "GET /api/books/search": answer(fixtures.match), ...quietSuggest })
    await renderApp("/", { strict: true })

    await userEvent.type(screen.getByLabelText("Describe the book"), "tolkien hobbit{Enter}")
    await waitFor(() => expect(view().kind).toBe("success"))
    await act(() => new Promise((r) => setTimeout(r, 50)))

    expect(searchCount(api)).toBe(1)
    expect(savedQueries()).toEqual(["tolkien hobbit"])
  })

  it("sends one request when the same text is submitted again while it runs", async () => {
    const search = manual()
    const api = fakeApi({ "GET /api/books/search": search.handler })
    await renderApp("/")

    typeAndSubmit("tolkien hobbit")
    act(() => session().submit())
    typeAndSubmit("  tolkien hobbit  ")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    await act(async () => search.requests[0].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))

    await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" }))
    expect(searchCount(api)).toBe(1)
    expect(search.requests[0].signal?.aborted).toBe(false)
  })

  it("asks for text instead of searching when the draft is blank", async () => {
    const api = fakeApi({})
    await renderApp("/")
    typeAndSubmit("   ")
    expect(session().fieldError).toBeTruthy()
    expect(view().kind).toBe("home")
    expect(api.calls).toHaveLength(0)
  })
})

describe("stale answers", () => {
  it.each(["before", "after"] as const)(
    "after Stop, a new search can't be overwritten by the stopped one answering late (%s the new answer)",
    async (order) => {
      const search = manual()
      fakeApi({ "GET /api/books/search": search.handler })
      const { router } = await renderApp("/")

      typeAndSubmit("dickens")
      await waitFor(() => expect(search.requests).toHaveLength(1))
      act(() => session().cancel())
      expect(view()).toMatchObject({ kind: "cancelled", query: "dickens" })
      expect(search.requests[0].signal?.aborted).toBe(true)

      typeAndSubmit("tolkien hobbit")
      await waitFor(() => expect(search.requests).toHaveLength(2))
      const stale = () => search.requests[0].respond(json({ ...fixtures.ambiguous(), query: "dickens" }))
      const fresh = () => search.requests[1].respond(json({ ...fixtures.match(), query: "tolkien hobbit" }))

      if (order === "before") {
        await act(async () => stale())
        expect(view()).toMatchObject({ kind: "pending", query: "tolkien hobbit" })
      }
      await act(async () => fresh())
      await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" }))
      if (order === "after") await act(async () => stale())

      expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit", response: { status: "match" } })
      expect(savedQueries()).toEqual(["tolkien hobbit"])
      expect(router.state.location.search).toEqual({ entry: historyStore.getSnapshot().entries[0].id })
    }
  )

  it("through the composer: Stop cancels, and the next Enter's answer wins over the stopped one", async () => {
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler, ...quietSuggest })
    await renderApp("/")

    await userEvent.type(screen.getByLabelText("Describe the book"), "dickens{Enter}")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    await userEvent.click(screen.getByRole("button", { name: "Stop" }))
    expect(view()).toMatchObject({ kind: "cancelled", query: "dickens" })
    expect(search.requests[0].signal?.aborted).toBe(true)

    const input = screen.getByLabelText("Describe the book")
    await userEvent.clear(input)
    await userEvent.type(input, "tolkien hobbit{Enter}")
    await waitFor(() => expect(search.requests).toHaveLength(2))
    await act(async () => search.requests[1].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))
    await act(async () => search.requests[0].respond(json({ ...fixtures.ambiguous(), query: "dickens" })))

    await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" }))
    expect(savedQueries()).toEqual(["tolkien hobbit"])
  })

  it("a new search while one runs aborts the first request and ignores its late answer", async () => {
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler })
    const { router } = await renderApp("/")

    typeAndSubmit("dickens")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(search.requests).toHaveLength(2))

    expect(search.requests[0].signal?.aborted).toBe(true)
    expect(search.requests[1].signal?.aborted).toBe(false)
    expect(view()).toMatchObject({ kind: "pending", query: "tolkien hobbit" })

    await act(async () => search.requests[1].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))
    await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" }))
    await act(async () => search.requests[0].respond(json({ ...fixtures.ambiguous(), query: "dickens" })))

    expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" })
    expect(savedQueries()).toEqual(["tolkien hobbit"])
    await waitFor(() =>
      expect(router.state.location.search).toEqual({ entry: historyStore.getSnapshot().entries[0].id })
    )
  })

  it("a stopped search that fails late doesn't replace the stopped state with an error", async () => {
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler })
    await renderApp("/")

    typeAndSubmit("dickens")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    act(() => session().cancel())
    await act(async () => search.requests[0].respond(problem(fixtures.catalogUnavailable(), { "Retry-After": "15" })))

    expect(view()).toMatchObject({ kind: "cancelled", query: "dickens" })
    expect(savedQueries()).toEqual([])
  })
})

describe("errors", () => {
  it.each([
    {},
    { degraded_reasons: "bad" },
    { candidates: "bad" },
    { ...fixtures.match(), candidates: [{ title: "Broken" }] },
  ])("a malformed successful response is recoverable", async (body) => {
    let malformed = true
    fakeApi({ "GET /api/books/search": () => json(malformed ? body : fixtures.match()) })
    await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view()).toMatchObject({ kind: "failed", error: { kind: "unexpected" } }))
    malformed = false
    act(() => session().retry())
    await waitFor(() => expect(view().kind).toBe("success"))
  })

  it("does not attach a delayed validation error to an edited draft", async () => {
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler })
    await renderApp("/")
    typeAndSubmit("the")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    act(() => session().setDraft("tolkien hobbit"))
    await act(async () => search.requests[0].respond(problem(fixtures.noSearchableTerms())))
    await waitFor(() => expect(view().kind).toBe("home"))
    expect(session().fieldError).toBeNull()
    act(() => session().submit())
    await waitFor(() => expect(search.requests).toHaveLength(2))
    await act(async () => search.requests[1].respond(json(fixtures.match())))
    await waitFor(() => expect(view().kind).toBe("success"))
  })

  it("a failed search keeps the answer that was on screen, as previous results", async () => {
    let offline = false
    fakeApi({
      "GET /api/books/search": (url) =>
        offline ? Promise.reject(new TypeError("Failed to fetch")) : answer(fixtures.match)(url, undefined),
      ...quietSuggest,
    })
    await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view().kind).toBe("success"))

    offline = true
    act(() => session().setDraft("dickens"))
    typeAndSubmit("dickens")
    await waitFor(() =>
      expect(view()).toMatchObject({ kind: "failed", query: "dickens", previous: { query: "tolkien hobbit" } })
    )
    expect(session().draft).toBe("dickens")
  })

  it.each([
    ["no_searchable_terms", fixtures.noSearchableTerms, "Add a title or author."],
    ["validation_failed", fixtures.validationFailed, "The query must be at most 300 characters."],
  ])("a 400 %s clears the previous answer and its URL entry", async (_code, body, message) => {
    const api = fakeApi({
      "GET /api/books/search": (url) =>
        url.searchParams.get("q") === "the"
          ? problem(body())
          : json({ ...fixtures.match(), query: url.searchParams.get("q") }),
    })
    const { router } = await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view().kind).toBe("success"))
    await waitFor(() => expect(router.state.location.search).toHaveProperty("entry"))

    typeAndSubmit("the")
    await waitFor(() => expect(session().fieldError).toBe(message))

    expect(view().kind).toBe("home")
    expect(session().draft).toBe("the")
    expect(savedQueries()).toEqual(["tolkien hobbit"])
    await waitFor(() => expect(router.state.location.search).not.toHaveProperty("entry"))
    expect(searchCount(api)).toBe(2)

    // Editing the text clears the message.
    act(() => session().setDraft("the hobbit"))
    expect(session().fieldError).toBeNull()
  })

  it("a 400 on the home page leaves the home page", async () => {
    fakeApi({ "GET /api/books/search": () => problem(fixtures.noSearchableTerms()) })
    await renderApp("/")
    typeAndSubmit("the")
    await waitFor(() => expect(session().fieldError).toBe("Add a title or author."))
    expect(view().kind).toBe("home")
  })

  it.each([
    [
      "503 with Retry-After",
      () => problem(fixtures.catalogUnavailable(), { "Retry-After": "15" }),
      { kind: "http", status: 503, code: "catalog_unavailable", retryAfterSeconds: 15 },
    ],
    [
      "429 with Retry-After",
      () => problem(fixtures.rateLimited(), { "Retry-After": "60" }),
      { kind: "http", status: 429, code: "rate_limited", retryAfterSeconds: 60 },
    ],
    [
      "504 search timeout",
      () => problem(fixtures.searchTimeout()),
      { kind: "http", status: 504, code: "search_timeout" },
    ],
    ["network failure", () => Promise.reject(new TypeError("Failed to fetch")), { kind: "network", status: null }],
    [
      "HTML instead of JSON",
      () => new Response("<!doctype html>", { headers: { "Content-Type": "text/html" } }),
      { kind: "unexpected" },
    ],
  ] as const)("%s shows the failed state, keeps the text and saves nothing", async (_name, respond, error) => {
    const api = fakeApi({ "GET /api/books/search": respond })
    const { router } = await renderApp("/")

    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view().kind).toBe("failed"))

    expect(view()).toMatchObject({ kind: "failed", query: "tolkien hobbit", error })
    expect(session().draft).toBe("tolkien hobbit")
    expect(session().fieldError).toBeNull()
    expect(savedQueries()).toEqual([])
    expect(router.state.location.search).toEqual({})
    expect(searchCount(api)).toBe(1)
  })

  it("retries a failed search only when asked, once", async () => {
    let fail = true
    const api = fakeApi({
      "GET /api/books/search": (url) =>
        fail ? problem(fixtures.catalogUnavailable(), { "Retry-After": "1" }) : answer(fixtures.match)(url, undefined),
    })
    await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view().kind).toBe("failed"))
    await act(() => new Promise((r) => setTimeout(r, 50)))
    expect(searchCount(api)).toBe(1)

    // Before Retry-After has passed nothing is sent, from any entry point; the composer says how long to wait.
    fail = false
    act(() => session().retry())
    expect(session().pausedUntil).toBeGreaterThan(Date.now())
    expect(session().fieldError).toBeNull()
    expect(searchCount(api)).toBe(1)

    await act(() => new Promise((r) => setTimeout(r, 1000)))
    act(() => session().retry())
    await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" }))
    expect(searchCount(api)).toBe(2)
  })
})

describe("history entries in the URL", () => {
  function seed(query: string, response: SearchResponse) {
    return historyStore.add(query, toSnapshot({ ...response, query }))
  }

  const isSearchOrSuggest = (c: { url: URL }) =>
    c.url.pathname === "/api/books/search" || c.url.pathname === "/api/suggest"

  it("restoring ?entry= shows the saved answer and its text without calling the API", async () => {
    const entry = seed("tolkien hobbit", fixtures.match())
    const api = fakeApi({})
    await renderApp(`/?entry=${entry.id}`)

    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } }))
    expect(session().draft).toBe("tolkien hobbit")
    expect(screen.getByLabelText("Describe the book")).toHaveValue("tolkien hobbit")
    await act(() => new Promise((r) => setTimeout(r, 50)))
    expect(api.calls.filter(isSearchOrSuggest)).toEqual([])
  })

  it("Back and Forward between two answers restore each one without calling the API", async () => {
    const api = fakeApi({
      "GET /api/books/search": (url) =>
        json({
          ...(url.searchParams.get("q") === "dickens" ? fixtures.ambiguous() : fixtures.match()),
          query: url.searchParams.get("q"),
        }),
    })
    const { router, history } = await renderApp("/")

    typeAndSubmit("dickens")
    await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "dickens" }))
    await waitFor(() => expect(router.state.location.search).toHaveProperty("entry"))
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" }))
    const [hobbit, dickens] = historyStore.getSnapshot().entries
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: hobbit.id }))

    act(() => history.back())
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: dickens.id } }))
    expect(session().draft).toBe("dickens")

    act(() => history.forward())
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: hobbit.id } }))
    expect(session().draft).toBe("tolkien hobbit")

    expect(searchCount(api)).toBe(2)
  })

  it("searching A, B, then A again keeps A's id, so Back twice restores B and then A", async () => {
    const api = fakeApi({
      "GET /api/books/search": (url) =>
        json({
          ...(url.searchParams.get("q") === "dickens" ? fixtures.ambiguous() : fixtures.match()),
          query: url.searchParams.get("q"),
        }),
    })
    const { router, history } = await renderApp("/")
    const ids: string[] = []
    for (const query of ["dickens", "tolkien hobbit", "dickens"]) {
      typeAndSubmit(query)
      await waitFor(() => expect(view()).toMatchObject({ kind: "success", query }))
      const id = historyStore.getSnapshot().entries[0].id
      await waitFor(() => expect(router.state.location.search).toEqual({ entry: id }))
      ids.push(id)
    }
    expect(ids[2]).toBe(ids[0])
    expect(savedQueries()).toEqual(["dickens", "tolkien hobbit"])

    act(() => history.back())
    await waitFor(() =>
      expect(view()).toMatchObject({ kind: "restored", entry: { id: ids[1], query: "tolkien hobbit" } })
    )
    act(() => history.back())
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: ids[0], query: "dickens" } }))
    expect(view()).toMatchObject({ entry: { result: { status: "ambiguous" } } })
    expect(searchCount(api)).toBe(3)
  })

  it("opening a saved answer while a search runs cancels that search for good", async () => {
    const entry = seed("dickens", fixtures.ambiguous())
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler })
    const { router } = await renderApp("/")

    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    await act(() => router.navigate({ to: "/", search: { entry: entry.id } }))
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } }))
    expect(search.requests[0].signal?.aborted).toBe(true)

    await act(async () => search.requests[0].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))
    expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } })
    expect(savedQueries()).toEqual(["dickens"])
  })

  it("an id that isn't saved on this device shows the missing-entry state", async () => {
    const api = fakeApi({})
    await renderApp("/?entry=abcdef123456")
    await waitFor(() => expect(view()).toEqual({ kind: "missing-entry", entryId: "abcdef123456" }))
    expect(api.calls.filter(isSearchOrSuggest)).toEqual([])
  })

  // TanStack Router merges raw params with validateSearch, so a bad id must come back as `undefined` to be dropped.
  it("a malformed ?entry= is ignored", async () => {
    const api = fakeApi({})
    const { router } = await renderApp("/?entry=%3Cscript%3E")
    expect(view().kind).toBe("home")
    await waitFor(() => expect(router.state.location.search).toEqual({}))
    expect(api.calls).toHaveLength(0)
  })

  it("going to /admin and back to /?entry=<id> keeps the live answer and fetches nothing", async () => {
    const api = fakeApi({ "GET /api/books/search": answer(fixtures.match) })
    const { router, history } = await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view().kind).toBe("success"))
    const { id } = historyStore.getSnapshot().entries[0]
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: id }))

    await act(() => router.navigate({ to: "/admin" }))
    expect(router.state.location.pathname).toBe("/admin")
    act(() => history.back())
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: id }))

    expect(view()).toMatchObject({ kind: "success", entryId: id })
    expect(searchCount(api)).toBe(1)
  })

  /** Starts a search, opens Admin, and lets the answer arrive there. */
  async function finishWhileOnAdmin() {
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler, "GET /api/admin/ai": () => json(adminStatus()) })
    const app = await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(search.requests).toHaveLength(1))

    await act(() => app.router.navigate({ to: "/admin" }))
    await act(async () => search.requests[0].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))
    await waitFor(() => expect(savedQueries()).toEqual(["tolkien hobbit"]))
    expect(app.router.state.location.pathname).toBe("/admin")
    return app
  }

  it("a search that finishes while Admin is open is on screen after Admin's Back to search", async () => {
    const { router } = await finishWhileOnAdmin()
    await userEvent.click(await screen.findByRole("link", { name: "Back to search" }))
    await waitFor(() =>
      expect(router.state.location.search).toEqual({ entry: historyStore.getSnapshot().entries[0].id })
    )
    expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" })
  })

  it("a search that finishes while Admin is open is on screen after the browser's Back", async () => {
    const { router, history } = await finishWhileOnAdmin()
    act(() => history.back())
    await waitFor(() => expect(router.state.location.pathname).toBe("/"))
    await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" }))
  })

  it("once that answer is back in the URL, the brand link to “/” goes home and stays there", async () => {
    const { router } = await finishWhileOnAdmin()
    const { id } = historyStore.getSnapshot().entries[0]
    await userEvent.click(await screen.findByRole("link", { name: "Back to search" }))
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: id }))

    await userEvent.click(screen.getByRole("link", { name: "Find That Book" }))
    await waitFor(() => expect(view()).toEqual({ kind: "home" }))
    await act(() => new Promise((r) => setTimeout(r, 50)))
    expect(router.state.location.href).toBe("/")
    expect(view()).toEqual({ kind: "home" })
  })

  it("browser Back from Admin to a saved answer's URL keeps a search that is still running, and shows its answer", async () => {
    const entry = seed("dickens", fixtures.ambiguous())
    const search = manual()
    fakeApi({
      "GET /api/books/search": search.handler,
      ...quietSuggest,
      "GET /api/admin/ai": () => json(adminStatus()),
    })
    const { router, history } = await renderApp(`/?entry=${entry.id}`)
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } }))
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(search.requests).toHaveLength(1))

    await userEvent.click(screen.getByRole("link", { name: "Admin" }))
    await waitFor(() => expect(router.state.location.pathname).toBe("/admin"))
    act(() => history.back())
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: entry.id }))

    expect(search.requests[0].signal?.aborted).toBe(false)
    expect(view()).toMatchObject({ kind: "pending", query: "tolkien hobbit" })
    await act(async () => search.requests[0].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))
    await waitFor(() => expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" }))
    const saved = historyStore.getSnapshot().entries[0]
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: saved.id }))
  })

  it("an answer that arrived on Admin replaces the old saved answer when Back returns to its URL", async () => {
    const entry = seed("dickens", fixtures.ambiguous())
    const search = manual()
    fakeApi({
      "GET /api/books/search": search.handler,
      ...quietSuggest,
      "GET /api/admin/ai": () => json(adminStatus()),
    })
    const { router, history } = await renderApp(`/?entry=${entry.id}`)
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored" }))
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    await userEvent.click(screen.getByRole("link", { name: "Admin" }))
    await waitFor(() => expect(router.state.location.pathname).toBe("/admin"))
    await act(async () => search.requests[0].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))
    await waitFor(() => expect(view()).toMatchObject({ kind: "success" }))

    act(() => history.back())
    const saved = historyStore.getSnapshot().entries.find((e) => e.query === "tolkien hobbit")!
    await waitFor(() => expect(router.state.location.search).toEqual({ entry: saved.id }))
    expect(view()).toMatchObject({ kind: "success", query: "tolkien hobbit" })
  })

  it("picking the entry on screen from History reloads it: another tab may have saved a newer answer under its id", async () => {
    const entry = seed("dickens", fixtures.ambiguous())
    fakeApi({ ...quietSuggest })
    await renderApp(`/?entry=${entry.id}`)
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } }))
    act(() => session().setDraft("an edited draft"))

    // Another tab repeats the query: same id, newer snapshot.
    const newer = historyStore.add("dickens", toSnapshot({ ...fixtures.match(), query: "dickens" }))
    expect(newer.id).toBe(entry.id)

    await userEvent.click(screen.getByRole("button", { name: "History" }))
    const sheet = await screen.findByRole("dialog", { name: "Search history" })
    await userEvent.click(within(sheet).getByRole("button", { name: "dickens" }))

    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { savedAt: newer.savedAt } }))
    expect(session().draft).toBe("dickens")
  })

  it("picking the entry already in the URL from History, while a newer search runs, restores it and drops that search", async () => {
    const entry = seed("dickens", fixtures.ambiguous())
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler, ...quietSuggest })
    const { router } = await renderApp(`/?entry=${entry.id}`)
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } }))
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    expect(router.state.location.search).toEqual({ entry: entry.id })

    await userEvent.click(screen.getByRole("button", { name: "History" }))
    const sheet = await screen.findByRole("dialog", { name: "Search history" })
    await userEvent.click(within(sheet).getByRole("button", { name: "dickens" }))

    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } }))
    expect(search.requests[0].signal?.aborted).toBe(true)
    await act(async () => search.requests[0].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))
    expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } })
    expect(savedQueries()).toEqual(["dickens"])
  })

  it("a 400 for a search started on a saved answer falls back to home if the user went Back to “/” meanwhile", async () => {
    const entry = seed("dickens", fixtures.ambiguous())
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler })
    const { router, history } = await renderApp("/")
    await act(() => router.navigate({ to: "/", search: { entry: entry.id } }))
    await waitFor(() => expect(view()).toMatchObject({ kind: "restored", entry: { id: entry.id } }))

    typeAndSubmit("the")
    await waitFor(() => expect(search.requests).toHaveLength(1))
    act(() => history.back())
    await waitFor(() => expect(router.state.location.search).toEqual({}))
    expect(view()).toMatchObject({ kind: "pending", query: "the" })

    await act(async () => search.requests[0].respond(problem(fixtures.noSearchableTerms())))
    await waitFor(() => expect(session().fieldError).toBe("Add a title or author."))
    expect(view()).toEqual({ kind: "home" })
  })
})

describe("starting over", () => {
  it("reset() goes back to the empty home state", async () => {
    fakeApi({ "GET /api/books/search": answer(fixtures.match) })
    await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(view().kind).toBe("success"))

    act(() => session().reset())
    expect(view()).toEqual({ kind: "home" })
    expect(session().draft).toBe("")
    expect(session().fieldError).toBeNull()
  })

  it("reset() during a search aborts it, and its late answer doesn't come back", async () => {
    const search = manual()
    fakeApi({ "GET /api/books/search": search.handler })
    await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(search.requests).toHaveLength(1))

    act(() => session().reset())
    expect(search.requests[0].signal?.aborted).toBe(true)
    await act(async () => search.requests[0].respond(json({ ...fixtures.match(), query: "tolkien hobbit" })))
    expect(view()).toEqual({ kind: "home" })
    expect(savedQueries()).toEqual([])
  })

  it("“New search” clears the answer and the ?entry= without fetching", async () => {
    const api = fakeApi({ "GET /api/books/search": answer(fixtures.match), ...quietSuggest })
    const { router } = await renderApp("/")
    typeAndSubmit("tolkien hobbit")
    await waitFor(() => expect(router.state.location.search).toHaveProperty("entry"))

    await userEvent.click(screen.getByRole("button", { name: "New search" }))
    await waitFor(() => expect(router.state.location.search).toEqual({}))
    expect(view()).toEqual({ kind: "home" })
    expect(session().draft).toBe("")
    expect(searchCount(api)).toBe(1)
  })
})
