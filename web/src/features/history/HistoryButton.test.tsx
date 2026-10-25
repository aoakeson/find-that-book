import { act, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { fakeApi, json, problem, type Handler } from "@/test/fake-api"
import { fixtures } from "@/test/fixtures"
import { renderApp } from "@/test/render"
import { toSnapshot } from "./snapshot"
import { HISTORY_STORAGE_KEY, HISTORY_VERSION, historyStore, type HistoryEntry } from "./storage"

const hobbit: HistoryEntry = {
  id: "hobbit000001",
  query: "tolkien hobbit illustrated deluxe 1937",
  savedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
  result: toSnapshot(fixtures.match()),
}
const dickens: HistoryEntry = {
  id: "dickens00001",
  query: "dickens",
  savedAt: new Date(Date.now() - 3 * 3_600_000).toISOString(),
  result: toSnapshot(fixtures.ambiguous()),
}
const nothing: HistoryEntry = {
  id: "nothing00001",
  query: "xqzv blorptangle",
  savedAt: new Date(Date.now() - 2 * 86_400_000).toISOString(),
  result: toSnapshot(fixtures.none()),
}

function seedHistory(...entries: HistoryEntry[]) {
  window.localStorage.setItem(HISTORY_STORAGE_KEY, JSON.stringify({ version: HISTORY_VERSION, entries }))
  historyStore.resetForTests()
}

/** Search would be a paid call: every test counts it. Suggestions and Admin status are free and stubbed. */
function api(search: Handler = () => json(fixtures.match())) {
  return fakeApi({
    "GET /api/books/search": search,
    "GET /api/suggest": () => json(fixtures.suggestEmpty()),
    "GET /api/admin/ai": () => problem({ status: 503 }),
  })
}

async function openHistory(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: "History" }))
  return screen.findByRole("dialog", { name: "Search history" })
}

beforeEach(() => historyStore.resetForTests())
afterEach(() => vi.restoreAllMocks())

describe("History", () => {
  it("lists a finished search with its outcome, marked as the one on screen", async () => {
    const calls = api()
    const user = userEvent.setup()
    const { router } = await renderApp("/")
    await user.type(screen.getByLabelText("Describe the book"), "tolkien hobbit{Enter}")
    await waitFor(() => expect(router.state.location.search).toHaveProperty("entry"))

    const sheet = await openHistory(user)
    expect(within(sheet).getByText("Saved on this device only.")).toBeVisible()
    const entry = within(sheet).getByRole("button", { name: "tolkien hobbit illustrated deluxe 1937" })
    expect(entry).toHaveAccessibleDescription("Best match: The Hobbit just now")
    expect(entry).toHaveAttribute("aria-current", "true")
    expect(
      within(sheet).getByRole("button", { name: "More actions for tolkien hobbit illustrated deluxe 1937" })
    ).toBeVisible()
    expect(calls.count("GET", "/api/books/search")).toBe(1)
  })

  it("shows entries newest first with a short outcome and the time saved", async () => {
    seedHistory(hobbit, dickens, nothing)
    api()
    const user = userEvent.setup()
    await renderApp("/")
    const sheet = await openHistory(user)

    const entries = within(sheet)
      .getAllByRole("listitem")
      .map((row) => within(row).getAllByRole("button")[0])
    expect(entries.map((b) => b.textContent)).toEqual([
      expect.stringContaining(hobbit.query),
      expect.stringContaining("dickens"),
      expect.stringContaining("xqzv blorptangle"),
    ])
    expect(entries[0]).toHaveAccessibleDescription("Best match: The Hobbit 5 minutes ago")
    expect(entries[1]).toHaveAccessibleDescription("5 possible books 3 hours ago")
    expect(entries[2]).toHaveAccessibleDescription("No match 2 days ago")
    // No entry is on screen yet, so none is current.
    expect(within(sheet).queryByRole("button", { current: true })).toBeNull()
    expect(within(sheet).getAllByRole("listitem")).toHaveLength(3)
    expect(sheet.querySelector(`time[datetime="${dickens.savedAt}"]`)).toHaveTextContent("3 hours ago")
  })

  it("restores an older entry without searching again", async () => {
    seedHistory(hobbit, dickens)
    const calls = api()
    const user = userEvent.setup()
    const { router } = await renderApp("/")
    const sheet = await openHistory(user)

    await user.click(within(sheet).getByRole("button", { name: "dickens" }))

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Search history" })).toBeNull())
    expect(router.state.location.search).toEqual({ entry: dickens.id })
    expect(await screen.findByRole("heading", { level: 3, name: "A Christmas Carol" })).toBeVisible()
    expect(screen.getByRole("button", { name: "Search again" })).toBeVisible()
    expect(calls.count("GET", "/api/books/search")).toBe(0)

    const reopened = await openHistory(user)
    expect(within(reopened).getByRole("button", { name: "dickens" })).toHaveAttribute("aria-current", "true")
  })

  it("deletes one entry from its menu and keeps focus in the list", async () => {
    seedHistory(hobbit, dickens)
    api()
    const user = userEvent.setup()
    await renderApp("/")
    const sheet = await openHistory(user)

    await user.click(within(sheet).getByRole("button", { name: `More actions for ${hobbit.query}` }))
    await user.click(await screen.findByRole("menuitem", { name: "Delete" }))

    expect(within(sheet).queryByRole("button", { name: hobbit.query })).toBeNull()
    const remaining = within(sheet).getByRole("button", { name: "dickens" })
    await waitFor(() => expect(remaining).toHaveFocus())
    expect(await screen.findByText("Search deleted")).toBeInTheDocument()
    expect(historyStore.getSnapshot().entries.map((e) => e.id)).toEqual([dickens.id])
    expect(JSON.parse(window.localStorage.getItem(HISTORY_STORAGE_KEY)!).entries).toHaveLength(1)
  })

  it("asks before clearing: Cancel keeps everything, Clear history removes it", async () => {
    seedHistory(hobbit, dickens)
    api()
    const user = userEvent.setup()
    await renderApp("/")
    const sheet = await openHistory(user)

    await user.click(within(sheet).getByRole("button", { name: "Clear history" }))
    let confirm = await screen.findByRole("alertdialog", { name: "Clear all saved searches?" })
    expect(confirm).toHaveAccessibleDescription("This removes 2 searches from this browser. It can't be undone.")
    await user.click(within(confirm).getByRole("button", { name: "Cancel" }))
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull())
    expect(within(sheet).getAllByRole("listitem")).toHaveLength(2)
    expect(historyStore.getSnapshot().entries).toHaveLength(2)

    await user.click(within(sheet).getByRole("button", { name: "Clear history" }))
    confirm = await screen.findByRole("alertdialog", { name: "Clear all saved searches?" })
    await user.click(within(confirm).getByRole("button", { name: "Clear history" }))

    expect(await within(sheet).findByText("No saved searches yet")).toBeVisible()
    await waitFor(() => expect(within(sheet).getByRole("button", { name: "Start a search" })).toHaveFocus())
    expect(await screen.findByText("History cleared")).toBeInTheDocument()
    expect(window.localStorage.getItem(HISTORY_STORAGE_KEY)).toBeNull()
    expect(within(sheet).queryByRole("button", { name: "Clear history" })).toBeNull()
  })

  it("offers one next step when empty: Start a search goes to the composer, from any page", async () => {
    api()
    const user = userEvent.setup()
    const { router } = await renderApp("/admin")
    const sheet = await openHistory(user)

    expect(within(sheet).getByText("No saved searches yet")).toBeVisible()
    expect(within(sheet).getByText("Searches you run are saved here, on this device only.")).toBeVisible()
    await user.click(within(sheet).getByRole("button", { name: "Start a search" }))

    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Search history" })).toBeNull())
    expect(router.state.location.pathname).toBe("/")
    await waitFor(() => expect(screen.getByLabelText("Describe the book")).toHaveFocus())
  })

  it.each([
    ["unavailable", "This browser isn't keeping history, so searches won't be saved here. Searching still works."],
    ["reset", "Saved history couldn't be read and was cleared."],
  ] as const)("explains a storage problem (%s) quietly inside the sheet", async (kind, message) => {
    if (kind === "reset") window.localStorage.setItem(HISTORY_STORAGE_KEY, "{broken")
    else
      vi.spyOn(window, "localStorage", "get").mockImplementation(() => {
        throw new DOMException("The operation is insecure.", "SecurityError")
      })
    api()
    const user = userEvent.setup()
    await renderApp("/")
    const sheet = await openHistory(user)
    expect(within(sheet).getByText(message)).toBeVisible()
    expect(screen.getByLabelText("Describe the book")).toBeEnabled()
  })

  it("follows changes made in another tab while open", async () => {
    seedHistory(dickens)
    api()
    const user = userEvent.setup()
    await renderApp("/")
    const sheet = await openHistory(user)

    const newValue = JSON.stringify({ version: HISTORY_VERSION, entries: [hobbit, dickens] })
    window.localStorage.setItem(HISTORY_STORAGE_KEY, newValue)
    act(() => {
      window.dispatchEvent(
        new StorageEvent("storage", { key: HISTORY_STORAGE_KEY, newValue, storageArea: window.localStorage })
      )
    })
    expect(within(sheet).getByRole("button", { name: hobbit.query })).toBeVisible()
    expect(within(sheet).getAllByRole("listitem")).toHaveLength(2)
  })

  it("shows the recovery for an ?entry= this browser doesn't have, without searching", async () => {
    const calls = api()
    await renderApp("/?entry=abcdef123456")
    expect(
      await screen.findByRole("heading", { level: 2, name: "This saved search isn't on this device" })
    ).toBeVisible()
    expect(screen.getByText("Back to search")).toBeVisible()
    expect(calls.count("GET", "/api/books/search")).toBe(0)
  })
})
