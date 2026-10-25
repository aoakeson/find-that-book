import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { fixtures } from "@/test/fixtures"
import { toSnapshot, type ResultSnapshot } from "./snapshot"
import { HISTORY_LIMIT, HISTORY_STORAGE_KEY, HISTORY_VERSION, historyStore, type HistoryEntry } from "./storage"

const snapshot = (): ResultSnapshot => toSnapshot(fixtures.match())

function stored(): { version: number; entries: HistoryEntry[] } | null {
  const raw = window.localStorage.getItem(HISTORY_STORAGE_KEY)
  return raw === null ? null : JSON.parse(raw)
}

function seed(value: unknown) {
  window.localStorage.setItem(HISTORY_STORAGE_KEY, typeof value === "string" ? value : JSON.stringify(value))
  historyStore.resetForTests()
}

function savedEntry(id: string, query: string, savedAt = "2026-10-24T18:00:00.000Z"): HistoryEntry {
  return { id, query, savedAt, result: snapshot() }
}

const quotaError = () => new DOMException("The quota has been exceeded.", "QuotaExceededError")

beforeEach(() => historyStore.resetForTests())
afterEach(() => vi.restoreAllMocks())

describe("historyStore", () => {
  it("never overwrites another tab's newer list, even if no storage event arrived", () => {
    historyStore.add("tolkien hobbit", snapshot())
    // Another tab saves a search; this tab missed the event (e.g. it was in the background).
    const other = savedEntry("othertab01", "dickens")
    window.localStorage.setItem(
      HISTORY_STORAGE_KEY,
      JSON.stringify({ version: HISTORY_VERSION, entries: [other, ...stored()!.entries] })
    )

    historyStore.add("austen bennet", snapshot())

    expect(stored()!.entries.map((e) => e.query)).toEqual(["austen bennet", "dickens", "tolkien hobbit"])
  })

  it("saves newest first under a versioned key and reads it back after a reload", () => {
    const first = historyStore.add("tolkien hobbit", snapshot())
    const second = historyStore.add("dickens", toSnapshot(fixtures.ambiguous()))

    expect(stored()?.version).toBe(HISTORY_VERSION)
    historyStore.resetForTests()
    const { entries, problem } = historyStore.getSnapshot()
    expect(problem).toBeNull()
    expect(entries.map((e) => e.id)).toEqual([second.id, first.id])
    expect(historyStore.get(first.id)?.result.candidates[0].title).toBe("The Hobbit")
  })

  it("stores only the allowlisted snapshot, even when handed a whole API response", () => {
    // A caller mistake TypeScript can't catch: a SearchResponse structurally satisfies ResultSnapshot.
    historyStore.add("tolkien hobbit", fixtures.match() as unknown as ResultSnapshot)
    const raw = window.localStorage.getItem(HISTORY_STORAGE_KEY) ?? ""
    for (const field of [
      "meta",
      "interpretation",
      "status_reason",
      "match_tier",
      "explanation_source",
      "duplicates_folded",
      "request_id",
    ]) {
      expect(raw).not.toContain(`"${field}"`)
    }
    expect(Object.keys(stored()!.entries[0].result).sort()).toEqual([
      "candidates",
      "degraded_reasons",
      "hint",
      "query",
      "status",
      "topics",
    ])
  })

  it("reads entries saved before topic requests existed as book requests", () => {
    const old: Record<string, unknown> = { ...snapshot() }
    delete old.topics
    old.candidates = snapshot().candidates.map((c) => {
      const copy: Record<string, unknown> = { ...c }
      delete copy.matched_subjects
      return copy
    })
    seed({ version: HISTORY_VERSION, entries: [{ ...savedEntry("oldentry01", "dickens"), result: old }] })

    const entry = historyStore.get("oldentry01")
    expect(entry?.result.topics).toEqual([])
    expect(entry?.result.candidates.every((c) => c.matched_subjects.length === 0)).toBe(true)
  })

  it("keeps at most 30 searches, dropping the oldest", () => {
    for (let i = 0; i < HISTORY_LIMIT + 5; i++) historyStore.add(`query ${i}`, snapshot())
    const { entries } = historyStore.getSnapshot()
    expect(entries).toHaveLength(HISTORY_LIMIT)
    expect(entries[0].query).toBe(`query ${HISTORY_LIMIT + 4}`)
    expect(entries.at(-1)?.query).toBe("query 5")
    expect(stored()?.entries).toHaveLength(HISTORY_LIMIT)
  })

  it("replaces an older entry for the same query, ignoring case and spacing", () => {
    historyStore.add("tolkien hobbit", snapshot())
    historyStore.add("dickens", snapshot())
    const again = historyStore.add("  Tolkien   HOBBIT ", snapshot())
    expect(historyStore.getSnapshot().entries.map((e) => e.id)).toEqual([again.id, expect.any(String)])
    expect(historyStore.getSnapshot().entries.map((e) => e.query)).toEqual(["  Tolkien   HOBBIT ", "dickens"])
  })

  it("removes one entry and clears all", () => {
    const a = historyStore.add("a query", snapshot())
    const b = historyStore.add("b query", snapshot())
    historyStore.remove(a.id)
    expect(historyStore.getSnapshot().entries.map((e) => e.id)).toEqual([b.id])
    historyStore.clear()
    expect(historyStore.getSnapshot()).toEqual({ entries: [], problem: null })
    expect(window.localStorage.getItem(HISTORY_STORAGE_KEY)).toBeNull()
  })

  describe("reading what is stored", () => {
    it("discards corrupt JSON and reports the reset", () => {
      seed("{not json")
      expect(historyStore.getSnapshot()).toEqual({ entries: [], problem: "reset" })
      expect(window.localStorage.getItem(HISTORY_STORAGE_KEY)).toBeNull()
    })

    it.each([
      ["another version", { version: 99, entries: [savedEntry("abcdef123456", "hobbit")] }],
      ["no version", [savedEntry("abcdef123456", "hobbit")]],
      ["entries not a list", { version: HISTORY_VERSION, entries: "nope" }],
      ["only invalid entries", { version: HISTORY_VERSION, entries: [{ id: "x" }, null] }],
    ])("discards %s and reports the reset", (_name, value) => {
      seed(value)
      expect(historyStore.getSnapshot()).toEqual({ entries: [], problem: "reset" })
      expect(window.localStorage.getItem(HISTORY_STORAGE_KEY)).toBeNull()
    })

    it("keeps the valid entries and quietly drops invalid ones", () => {
      const good = savedEntry("good0000001", "tolkien hobbit")
      const badCandidate = savedEntry("bad00000001", "bad candidate")
      ;(badCandidate.result.candidates[0] as unknown as Record<string, unknown>).rank = "1"
      const badStatus = { ...savedEntry("bad00000002", "bad status"), result: { ...snapshot(), status: "maybe" } }
      const badDate = savedEntry("bad00000003", "bad date", "yesterday")
      const badId = savedEntry("NOT-AN-ID", "bad id")
      const badReason = {
        ...savedEntry("bad00000004", "bad reason"),
        result: { ...snapshot(), degraded_reasons: ["gremlins"] },
      }
      seed({ version: HISTORY_VERSION, entries: [badCandidate, good, badStatus, badDate, badId, badReason, "junk"] })

      const { entries, problem } = historyStore.getSnapshot()
      expect(problem).toBeNull()
      expect(entries.map((e) => e.id)).toEqual(["good0000001"])
    })

    it("strips unknown fields from stored entries", () => {
      const entry = savedEntry("abcdef123456", "hobbit")
      seed({
        version: HISTORY_VERSION,
        entries: [{ ...entry, secret: "x", result: { ...entry.result, meta: { llm: {} } } }],
      })
      const [read] = historyStore.getSnapshot().entries
      expect(read).not.toHaveProperty("secret")
      expect(read.result).not.toHaveProperty("meta")
    })

    it("drops repeated ids and queries, keeping the newer, and applies the limit without calling it a reset", () => {
      const entries = [
        savedEntry("first000001", "Hobbit"),
        savedEntry("first000001", "something else"),
        savedEntry("second00001", "hobbit"),
        ...Array.from({ length: HISTORY_LIMIT + 3 }, (_, i) =>
          savedEntry(`fill${String(i).padStart(7, "0")}`, `fill ${i}`)
        ),
      ]
      seed({ version: HISTORY_VERSION, entries })
      const state = historyStore.getSnapshot()
      expect(state.problem).toBeNull()
      expect(state.entries).toHaveLength(HISTORY_LIMIT)
      expect(state.entries[0].id).toBe("first000001")
      expect(state.entries.filter((e) => e.query.toLowerCase() === "hobbit")).toHaveLength(1)
    })
  })

  describe("when the browser refuses", () => {
    it("drops the oldest entries when the quota is tight, keeping the newest", () => {
      for (let i = 0; i < 10; i++) historyStore.add(`query ${i}`, snapshot())
      const roomFor =
        JSON.stringify({ version: HISTORY_VERSION, entries: historyStore.getSnapshot().entries.slice(0, 4) }).length +
        50
      const realSetItem = Storage.prototype.setItem
      vi.spyOn(Storage.prototype, "setItem").mockImplementation(function (this: Storage, key: string, value: string) {
        if (value.length > roomFor) throw quotaError()
        realSetItem.call(this, key, value)
      })

      const newest = historyStore.add("the newest one", snapshot())
      const { entries, problem } = historyStore.getSnapshot()
      expect(problem).toBeNull()
      expect(entries[0].id).toBe(newest.id)
      expect(entries.length).toBeLessThan(11)
      expect(entries.length).toBeGreaterThan(0)
      expect(stored()?.entries.map((e) => e.id)).toEqual(entries.map((e) => e.id))
    })

    it("reports full when nothing fits, keeping the new entry for this visit and the stored list untouched", () => {
      const old = historyStore.add("older search", snapshot())
      const before = window.localStorage.getItem(HISTORY_STORAGE_KEY)
      const setItem = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
        throw quotaError()
      })

      const entry = historyStore.add("tolkien hobbit", snapshot())
      expect(setItem).toHaveBeenCalled()
      expect(historyStore.getSnapshot().problem).toBe("full")
      expect(historyStore.get(entry.id)?.query).toBe("tolkien hobbit")
      expect(historyStore.get(old.id)).toBeDefined()
      expect(window.localStorage.getItem(HISTORY_STORAGE_KEY)).toBe(before)

      setItem.mockRestore()
      historyStore.add("room again", snapshot())
      expect(historyStore.getSnapshot().problem).toBeNull()
    })

    it("works in memory when localStorage throws on access", () => {
      vi.spyOn(window, "localStorage", "get").mockImplementation(() => {
        throw new DOMException("The operation is insecure.", "SecurityError")
      })

      expect(historyStore.getSnapshot()).toEqual({ entries: [], problem: "unavailable" })
      const entry = historyStore.add("tolkien hobbit", snapshot())
      expect(historyStore.get(entry.id)?.query).toBe("tolkien hobbit")
      expect(historyStore.getSnapshot().problem).toBe("unavailable")
      historyStore.remove(entry.id)
      expect(historyStore.getSnapshot().entries).toEqual([])
      expect(() => historyStore.clear()).not.toThrow()
    })

    it("reports unavailable when a write fails for a reason other than quota", () => {
      vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
        throw new DOMException("The operation is insecure.", "SecurityError")
      })
      const entry = historyStore.add("tolkien hobbit", snapshot())
      expect(historyStore.getSnapshot().problem).toBe("unavailable")
      expect(historyStore.get(entry.id)).toBeDefined()
    })
  })

  describe("other tabs", () => {
    it("picks up another tab's change and clear", () => {
      const listener = vi.fn()
      const unsubscribe = historyStore.subscribe(listener)
      expect(historyStore.getSnapshot().entries).toEqual([])

      const fromOtherTab = savedEntry("othertab0001", "dickens")
      const newValue = JSON.stringify({ version: HISTORY_VERSION, entries: [fromOtherTab] })
      window.localStorage.setItem(HISTORY_STORAGE_KEY, newValue)
      window.dispatchEvent(
        new StorageEvent("storage", { key: HISTORY_STORAGE_KEY, newValue, storageArea: window.localStorage })
      )

      expect(listener).toHaveBeenCalledTimes(1)
      expect(historyStore.getSnapshot().entries.map((e) => e.id)).toEqual(["othertab0001"])

      window.localStorage.clear()
      window.dispatchEvent(new StorageEvent("storage", { key: null, storageArea: window.localStorage }))
      expect(listener).toHaveBeenCalledTimes(2)
      expect(historyStore.getSnapshot().entries).toEqual([])
      unsubscribe()
    })

    it("ignores other keys and sessionStorage", () => {
      const listener = vi.fn()
      const unsubscribe = historyStore.subscribe(listener)
      window.dispatchEvent(
        new StorageEvent("storage", { key: "ftb.activityOpen", newValue: "1", storageArea: window.localStorage })
      )
      window.dispatchEvent(
        new StorageEvent("storage", { key: HISTORY_STORAGE_KEY, newValue: "[]", storageArea: window.sessionStorage })
      )
      expect(listener).not.toHaveBeenCalled()
      unsubscribe()
    })
  })
})
