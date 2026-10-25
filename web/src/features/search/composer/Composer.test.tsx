import { act, fireEvent, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { flushSync } from "react-dom"
import { describe, expect, it, onTestFinished, vi } from "vitest"
import type { SuggestResponse } from "@/api/types"
import { deferred, fakeApi, json, problem, type Handler } from "@/test/fake-api"
import { fixtures } from "@/test/fixtures"
import { renderApp } from "@/test/render"

const composer = () => screen.getByRole("combobox", { name: "Describe the book" })
const searchCalls = (api: ReturnType<typeof fakeApi>) => api.count("GET", "/api/books/search")
const pause = (ms: number) => act(() => new Promise<void>((resolve) => setTimeout(resolve, ms)))

function suggestionsFor(prefix: string, titles: string[]): SuggestResponse {
  return {
    prefix,
    suggestions: titles.map((title, i) => ({
      work_id: `OL${i + 1}W`,
      title,
      author: null,
      first_publish_year: null,
      cover_url: null,
      edition_count: 1,
      open_library_url: `https://openlibrary.org/works/OL${i + 1}W`,
      match: "title" as const,
    })),
  }
}

const harSuggestions: Handler = () => json(fixtures.suggestHar())

describe("suggestions", () => {
  it("shows works from GET /api/suggest for the text the user paused on", async () => {
    const api = fakeApi({ "GET /api/suggest": harSuggestions })
    const user = userEvent.setup()
    await renderApp("/")
    const [first] = fixtures.suggestHar().suggestions

    await user.type(composer(), "har")

    const options = within(await screen.findByRole("listbox")).getAllByRole("option")
    expect(options).toHaveLength(fixtures.suggestHar().suggestions.length)
    expect(options[0]).toHaveAccessibleName(
      `${first.title} by ${first.author}, first published ${first.first_publish_year}`
    )
    expect(composer()).toHaveAttribute("aria-expanded", "true")
    // Debounced: one request, for the whole prefix, with the contract's limit.
    expect(api.count("GET", "/api/suggest")).toBe(1)
    const { url } = api.calls.find((c) => c.url.pathname === "/api/suggest")!
    expect(url.searchParams.get("prefix")).toBe("har")
    expect(url.searchParams.get("limit")).toBe("6")
  })

  it("ArrowDown then Enter fills the composer with title and author, keeps focus, and does not search", async () => {
    const api = fakeApi({ "GET /api/suggest": harSuggestions, "GET /api/books/search": () => json(fixtures.match()) })
    const user = userEvent.setup()
    await renderApp("/")
    const [first] = fixtures.suggestHar().suggestions

    await user.type(composer(), "har")
    const firstOption = within(await screen.findByRole("listbox")).getAllByRole("option")[0]
    await user.keyboard("{ArrowDown}")
    expect(composer()).toHaveAttribute("aria-activedescendant", firstOption.id)
    await user.keyboard("{Enter}")

    expect(composer()).toHaveValue(`${first.title} ${first.author}`)
    expect(composer()).toHaveFocus()
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument()
    await pause(50)
    expect(searchCalls(api)).toBe(0)
  })

  it("clicking a suggestion fills the composer without searching; missing author and year are left out, not invented", async () => {
    const response = suggestionsFor("anna", ["Anna Karenina", "Anonymous Tales"])
    response.suggestions[0] = { ...response.suggestions[0], author: "Leo Tolstoy", first_publish_year: 1878 }
    const api = fakeApi({
      "GET /api/suggest": () => json(response),
      "GET /api/books/search": () => json(fixtures.match()),
    })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "anna")
    expect(
      await screen.findByRole("option", { name: "Anna Karenina by Leo Tolstoy, first published 1878" })
    ).toBeInTheDocument()
    const bare = screen.getByRole("option", { name: "Anonymous Tales" })
    expect(bare).toHaveTextContent(/^Anonymous Tales$/)
    await user.click(bare)

    expect(composer()).toHaveValue("Anonymous Tales")
    expect(composer()).toHaveFocus()
    expect(searchCalls(api)).toBe(0)
  })

  it("Enter with no suggestion highlighted runs one search for the typed text", async () => {
    const api = fakeApi({ "GET /api/suggest": harSuggestions, "GET /api/books/search": () => json(fixtures.match()) })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "har")
    await screen.findByRole("listbox")
    await user.keyboard("{Enter}")

    await waitFor(() => expect(searchCalls(api)).toBe(1))
    expect(api.calls.find((c) => c.url.pathname === "/api/books/search")!.url.searchParams.get("q")).toBe("har")
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument()
    await pause(50)
    expect(searchCalls(api)).toBe(1)
  })

  it("Escape closes the list and never changes the text", async () => {
    fakeApi({ "GET /api/suggest": harSuggestions })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "har")
    await screen.findByRole("listbox")
    await user.keyboard("{Escape}")
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument()
    expect(composer()).toHaveValue("har")

    // A second Escape (list already closed) would clear a plain Base UI autocomplete; here it keeps the text.
    await user.keyboard("{Escape}")
    expect(composer()).toHaveValue("har")
    expect(composer()).toHaveFocus()
  })

  it("the Enter that confirms an IME composition neither submits nor accepts", async () => {
    const api = fakeApi({
      "GET /api/suggest": () => json(suggestionsFor("三体", ["三体"])),
      "GET /api/books/search": () => json(fixtures.match()),
    })
    const user = userEvent.setup()
    await renderApp("/")
    const input = composer()
    input.focus()

    fireEvent.compositionStart(input)
    fireEvent.change(input, { target: { value: "三体" } })
    // Chrome/Firefox flag the confirming keydown as composing; `false` = default prevented, so no implicit submit.
    expect(fireEvent.keyDown(input, { key: "Enter", code: "Enter", keyCode: 229, isComposing: true })).toBe(false)
    fireEvent.compositionEnd(input)
    // Safari ends the composition first, then sends an unflagged Enter keydown.
    expect(fireEvent.keyDown(input, { key: "Enter", code: "Enter", keyCode: 13 })).toBe(false)

    expect(input).toHaveValue("三体")
    expect(searchCalls(api)).toBe(0)

    // The user's own Enter afterwards searches as usual (short Unicode queries are allowed).
    await pause(80)
    await user.keyboard("{Enter}")
    await waitFor(() => expect(searchCalls(api)).toBe(1))
    expect(api.calls.find((c) => c.url.pathname === "/api/books/search")!.url.searchParams.get("q")).toBe("三体")
  })

  it("keeps the shown list (marked busy) while the next prefix loads, then replaces it, and closes when nothing matches", async () => {
    const harr = deferred()
    const api = fakeApi({
      "GET /api/suggest": (url, init) => {
        const prefix = url.searchParams.get("prefix")!
        if (prefix === "har") return json(suggestionsFor(prefix, ["Harry Potter", "Harriet the Spy"]))
        if (prefix === "harr") return harr.handler(url, init)
        return json(suggestionsFor(prefix, []))
      },
    })
    const user = userEvent.setup()
    await renderApp("/")
    const busyPopup = () => screen.queryByRole("listbox")?.closest('[aria-busy="true"]') ?? null

    await user.type(composer(), "har")
    expect(await screen.findByRole("option", { name: "Harry Potter" })).toBeInTheDocument()
    expect(busyPopup()).toBeNull()

    await user.type(composer(), "r")
    await waitFor(() => expect(api.count("GET", "/api/suggest")).toBe(2))
    // No blink: the previous list stays while "harr" is on its way.
    expect(screen.getByRole("option", { name: "Harry Potter" })).toBeInTheDocument()
    expect(busyPopup()).not.toBeNull()

    harr.resolve(json(suggestionsFor("harr", ["Harrow County"])))
    expect(await screen.findByRole("option", { name: "Harrow County" })).toBeInTheDocument()
    expect(screen.queryByRole("option", { name: "Harry Potter" })).not.toBeInTheDocument()
    expect(busyPopup()).toBeNull()

    await user.type(composer(), "x")
    await waitFor(() => expect(screen.queryByRole("listbox")).not.toBeInTheDocument())
    expect(composer()).toHaveAttribute("aria-expanded", "false")
  })

  it("an emptied field, or a dismissed list, keeps nothing to show later", async () => {
    const later = deferred()
    fakeApi({
      "GET /api/suggest": (url, init) =>
        url.searchParams.get("prefix") === "har" ? json(fixtures.suggestHar()) : later.handler(url, init),
    })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "har")
    await screen.findByRole("listbox")
    await user.clear(composer())
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument()

    await user.type(composer(), "har")
    await screen.findByRole("listbox")
    await user.keyboard("{Escape}")
    // Typing again after Escape waits for the new text's own answer instead of reviving the dismissed list.
    await user.type(composer(), "d")
    await pause(200)
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument()
  })

  it("a slow answer for an older prefix never replaces the current prefix's suggestions", async () => {
    let releaseOld!: () => void
    let oldSignal: AbortSignal | null | undefined
    fakeApi({
      "GET /api/suggest": (url, init) => {
        if (url.searchParams.get("prefix") === "har") {
          oldSignal = init?.signal
          // Answers late even after being aborted, like a server that ignores the cancellation.
          return new Promise<Response>((resolve) => (releaseOld = () => resolve(json(fixtures.suggestHar()))))
        }
        return json(suggestionsFor("harr", ["Harriet the Spy"]))
      },
    })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "har")
    await waitFor(() => expect(oldSignal).toBeDefined())
    await user.type(composer(), "r")

    expect(await screen.findByRole("option", { name: "Harriet the Spy" })).toBeInTheDocument()
    expect(oldSignal?.aborted).toBe(true)

    releaseOld()
    await pause(50)
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual(["Harriet the Spy"])
  })

  it.each([
    ["an error status", (() => problem({ status: 503 })) as Handler],
    ["a network failure", (() => Promise.reject(new TypeError("Failed to fetch"))) as Handler],
  ])("when suggestions fail with %s, nothing pops up and search still works", async (_name, failure) => {
    const api = fakeApi({ "GET /api/suggest": failure, "GET /api/books/search": () => json(fixtures.match()) })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "har")
    await waitFor(() => expect(api.count("GET", "/api/suggest")).toBe(1))
    await pause(50)
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument()
    expect(composer()).toHaveAttribute("aria-expanded", "false")
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()

    await user.keyboard("{Enter}")
    await waitFor(() => expect(searchCalls(api)).toBe(1))
  })
})

describe("search button", () => {
  /**
   * In a browser, React renders a click's state update (a microtask) before the button's default action runs; jsdom
   * runs the default action first. This listener flushes React at the browser's moment, so a button that changes
   * type mid-click behaves here as it would for a real user.
   */
  function withBrowserClickTiming() {
    const flush = () => flushSync(() => {})
    document.addEventListener("click", flush)
    return () => document.removeEventListener("click", flush)
  }

  it("becomes Stop while a search runs; Stop cancels it without starting another search", async () => {
    let searchSignal: AbortSignal | null | undefined
    const search = deferred()
    const api = fakeApi({
      "GET /api/suggest": () => json(fixtures.suggestEmpty()),
      "GET /api/books/search": (url, init) => {
        searchSignal = init?.signal
        return search.handler(url, init)
      },
    })
    const user = userEvent.setup()
    await renderApp("/")
    onTestFinished(withBrowserClickTiming())

    await user.type(composer(), "tolkien hobbit{Enter}")
    const stop = await screen.findByRole("button", { name: "Stop" })
    expect(screen.queryByRole("button", { name: "Search" })).not.toBeInTheDocument()

    await user.click(stop)

    // Regression: the Stop click once fell through to the form (the same <button> had become type="submit").
    expect(await screen.findAllByText("Search stopped")).not.toHaveLength(0)
    expect(screen.getByRole("button", { name: "Search" })).toBeInTheDocument()
    expect(searchSignal?.aborted).toBe(true)
    expect(composer()).toHaveValue("tolkien hobbit")
    await pause(50)
    expect(searchCalls(api)).toBe(1)
  })

  it("Stop works from the keyboard too", async () => {
    const search = deferred()
    const api = fakeApi({
      "GET /api/suggest": () => json(fixtures.suggestEmpty()),
      "GET /api/books/search": search.handler,
    })
    const user = userEvent.setup()
    await renderApp("/")
    onTestFinished(withBrowserClickTiming())

    await user.type(composer(), "tolkien hobbit{Enter}")
    await user.tab()
    expect(screen.getByRole("button", { name: "Stop" })).toHaveFocus()
    await user.keyboard("{Enter}")

    expect(await screen.findAllByText("Search stopped")).not.toHaveLength(0)
    await pause(50)
    expect(searchCalls(api)).toBe(1)
  })

  it("keeps keyboard focus on the button as Search turns into Stop and back", async () => {
    const search = deferred()
    fakeApi({ "GET /api/suggest": () => json(fixtures.suggestEmpty()), "GET /api/books/search": search.handler })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "tolkien hobbit")
    await user.tab()
    await user.keyboard("{Enter}")
    expect(await screen.findByRole("button", { name: "Stop" })).toHaveFocus()

    search.resolve(json(fixtures.match()))
    expect(await screen.findByRole("button", { name: "Search" })).toHaveFocus()
  })

  it("the second click of a double-click on Search does not stop the search it started", async () => {
    const search = deferred()
    const api = fakeApi({
      "GET /api/suggest": () => json(fixtures.suggestEmpty()),
      "GET /api/books/search": search.handler,
    })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "tolkien hobbit")
    await user.click(screen.getByRole("button", { name: "Search" }))
    // The browser reports the second click (detail 2) on whatever is now under the pointer: the Stop button.
    fireEvent.click(await screen.findByRole("button", { name: "Stop" }), { detail: 2 })

    expect(screen.getByRole("button", { name: "Stop" })).toBeInTheDocument()
    expect(searchCalls(api)).toBe(1)
  })
})

describe("field errors", () => {
  it("shows the API's 400 for the text under the composer, linked to the input, until the text changes", async () => {
    // The API's own no_searchable_terms problem, with a message pinned here so the test doesn't track example wording.
    const message = "Add a title or author."
    const rejected = { ...fixtures.noSearchableTerms(), errors: { Q: [message] } }
    fakeApi({
      "GET /api/suggest": () => json(fixtures.suggestEmpty()),
      "GET /api/books/search": () => problem(rejected),
    })
    const user = userEvent.setup()
    await renderApp("/")

    await user.type(composer(), "the{Enter}")

    expect(await screen.findByText(message)).toBeInTheDocument()
    expect(composer()).toHaveAttribute("aria-invalid", "true")
    expect(composer()).toHaveAccessibleDescription(message)
    expect(composer()).toHaveValue("the")

    await user.type(composer(), " hobbit")
    expect(screen.queryByText(message)).not.toBeInTheDocument()
    expect(composer()).not.toHaveAttribute("aria-invalid")
  })

  it("an empty search explains what to type and calls nothing", async () => {
    const api = fakeApi({})
    const user = userEvent.setup()
    await renderApp("/")

    await user.click(screen.getByRole("button", { name: "Search" }))

    expect(composer()).toHaveAccessibleDescription("Type a title, an author, or any detail you remember.")
    expect(api.calls).toHaveLength(0)
  })
})

describe("waiting after Retry-After", () => {
  it("refuses a search with a neutral countdown, announced once, then searches again once the wait is over", async () => {
    const api = fakeApi({
      "GET /api/suggest": () => json(fixtures.suggestEmpty()),
      "GET /api/books/search": () =>
        searchCalls(api) === 1
          ? problem(fixtures.catalogUnavailable(), { "Retry-After": "2" })
          : json(fixtures.match()),
    })
    await renderApp("/")
    await userEvent.setup().type(composer(), "tolkien hobbit{Enter}")
    expect(await screen.findByText("Open Library is unavailable")).toBeVisible()

    // Fake clock from here (Testing Library's async helpers still need time to pass, hence shouldAdvanceTime).
    vi.useFakeTimers({ shouldAdvanceTime: true })
    onTestFinished(() => void vi.useRealTimers())
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    await user.keyboard("{Enter}")

    const visible = screen.getByText("Searching resumes in 2 s.")
    const region = visible.closest<HTMLElement>('[aria-live="polite"]')!
    expect(region).toBeInTheDocument()
    // The ticking number is hidden from assistive tech; the region holds one fixed sentence.
    expect(visible).toHaveAttribute("aria-hidden", "true")
    expect(within(region).getByText("Searching resumes in 2 seconds.")).toBeInTheDocument()
    expect(composer()).not.toHaveAttribute("aria-invalid")
    expect(composer()).not.toHaveAccessibleDescription()
    expect(searchCalls(api)).toBe(1)

    act(() => vi.advanceTimersByTime(1000))
    expect(screen.getByText("Searching resumes in 1 s.")).toBeInTheDocument()
    expect(within(region).getByText("Searching resumes in 2 seconds.")).toBeInTheDocument()

    act(() => vi.advanceTimersByTime(1000))
    expect(screen.queryByText(/Searching resumes/)).not.toBeInTheDocument()

    await user.keyboard("{Enter}")
    await act(() => vi.advanceTimersByTimeAsync(50))
    expect(searchCalls(api)).toBe(2)
  })
})
