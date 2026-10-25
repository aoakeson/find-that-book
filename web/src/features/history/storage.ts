import {
  DEGRADED_REASONS,
  MATCH_STATUSES,
  type AuthorLink,
  type ContributorLink,
  type DegradedReason,
} from "@/api/types"
import type { CandidateSnapshot, ResultSnapshot } from "./snapshot"

/**
 * Search history kept in this browser only: a small, bounded, versioned list in localStorage.
 * Storage may be missing (privacy modes, blocked site data), full, corrupt or from an older version; every case falls
 * back to an in-memory list so the rest of the app keeps working. Nothing here ever throws to a caller.
 * Only the allowlisted result snapshot is kept: fields are copied one by one on the way in and on the way out, so
 * diagnostics or anything else a caller passes along never reach storage.
 */

export const HISTORY_STORAGE_KEY = "ftb.history"
export const HISTORY_VERSION = 1
export const HISTORY_LIMIT = 30

export interface HistoryEntry {
  /** Local id, also used in the URL (?entry=). */
  id: string
  /** The query exactly as the API searched it. */
  query: string
  /** ISO timestamp. */
  savedAt: string
  result: ResultSnapshot
}

/** null: saving works. unavailable: no storage at all. full: storage refused the write. reset: unreadable data was discarded. */
export type HistoryProblem = "unavailable" | "full" | "reset" | null

export interface HistoryState {
  entries: readonly HistoryEntry[]
  problem: HistoryProblem
}

type Listener = () => void

const listeners = new Set<Listener>()
let state: HistoryState | null = null

function storage(): Storage | null {
  try {
    // The getter itself throws (SecurityError) when site data is blocked. No write probe: a full quota is not "no storage".
    const s = window.localStorage
    return s && typeof s.getItem === "function" ? s : null
  } catch {
    return null
  }
}

function load(): HistoryState {
  const s = storage()
  if (!s) return { entries: [], problem: "unavailable" }
  let raw: string | null
  try {
    raw = s.getItem(HISTORY_STORAGE_KEY)
  } catch {
    return { entries: [], problem: "unavailable" }
  }
  if (raw === null) return { entries: [], problem: null }
  const entries = parse(raw)
  if (entries) return { entries, problem: null }
  try {
    s.removeItem(HISTORY_STORAGE_KEY)
  } catch {
    // Nothing more to do: the in-memory list still works.
  }
  return { entries: [], problem: "reset" }
}

/** The stored entries, or null when the stored value as a whole can't be used (corrupt, other version, nothing valid). */
function parse(raw: string): HistoryEntry[] | null {
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return null
  }
  if (!isObject(parsed) || parsed.version !== HISTORY_VERSION || !Array.isArray(parsed.entries)) return null
  const valid = parsed.entries.map(readEntry).filter((e): e is HistoryEntry => e !== null)
  // A few bad entries among good ones are dropped quietly; only a wholly unreadable list is reported.
  if (valid.length === 0 && parsed.entries.length > 0) return null
  return tidy(valid)
}

/** Newest first as stored: drops repeated ids and repeated queries (the newer one wins), then applies the limit. */
function tidy(entries: readonly HistoryEntry[]): HistoryEntry[] {
  const ids = new Set<string>()
  const queries = new Set<string>()
  const out: HistoryEntry[] = []
  for (const entry of entries) {
    const key = queryKey(entry.query)
    if (ids.has(entry.id) || queries.has(key)) continue
    ids.add(entry.id)
    queries.add(key)
    out.push(entry)
    if (out.length === HISTORY_LIMIT) break
  }
  return out
}

/** Saves as much of the list as the browser accepts. A tight quota drops the oldest entries before giving up. */
function persist(entries: readonly HistoryEntry[]): HistoryState {
  const s = storage()
  if (!s) return { entries, problem: "unavailable" }
  if (entries.length === 0) {
    try {
      s.removeItem(HISTORY_STORAGE_KEY)
      return { entries, problem: null }
    } catch {
      return { entries, problem: "unavailable" }
    }
  }
  for (let count = entries.length; count > 0; count--) {
    const kept = entries.slice(0, count)
    let serialized: string
    try {
      serialized = JSON.stringify({ version: HISTORY_VERSION, entries: kept })
    } catch {
      return { entries, problem: "full" }
    }
    try {
      s.setItem(HISTORY_STORAGE_KEY, serialized)
      return { entries: kept, problem: null }
    } catch (error) {
      if (!isQuotaError(error)) return { entries, problem: "unavailable" }
    }
  }
  // Not even the newest entry fits. Keep the whole list in memory so this visit still works.
  return { entries, problem: "full" }
}

function isQuotaError(error: unknown): boolean {
  if (!(error instanceof DOMException)) return false
  return (
    error.name === "QuotaExceededError" ||
    error.name === "NS_ERROR_DOM_QUOTA_REACHED" ||
    error.code === 22 ||
    error.code === 1014
  )
}

let watching = false

function current(): HistoryState {
  // Watch other tabs for the app's whole lifetime, not only while History is open, so this copy never goes stale.
  if (!watching) {
    window.addEventListener("storage", onStorage)
    watching = true
  }
  if (state === null) state = load()
  return state
}

/**
 * Before a change: re-read storage when it is in sync, so a change another tab made earlier is kept. (Two tabs writing
 * at the same instant still end last-writer-wins: localStorage has no lock, and a 30-entry convenience list doesn't
 * warrant one.)
 */
function fresh(): HistoryState {
  if (current().problem === null) state = load()
  return current()
}

function emit() {
  for (const listener of [...listeners]) listener()
}

function update(entries: readonly HistoryEntry[]) {
  state = persist(entries)
  emit()
}

/** Another tab changed (or cleared) this origin's storage: re-read it so every tab shows the same list. */
function onStorage(event: StorageEvent) {
  if (event.key !== null && event.key !== HISTORY_STORAGE_KEY) return
  if (event.storageArea && event.storageArea !== storage()) return
  state = load()
  emit()
}

export const historyStore = {
  subscribe(listener: Listener): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },

  getSnapshot(): HistoryState {
    return current()
  },

  get(id: string): HistoryEntry | undefined {
    return current().entries.find((e) => e.id === id)
  },

  /**
   * Newest first; a repeat of the same query (ignoring case and spacing) replaces its older entry but keeps its id,
   * so earlier ?entry= links in the browser history still resolve.
   */
  add(query: string, result: ResultSnapshot, now = new Date()): HistoryEntry {
    const previous = fresh().entries.find((e) => queryKey(e.query) === queryKey(query))
    const entry: HistoryEntry = { id: previous?.id ?? newId(), query, savedAt: now.toISOString(), result }
    const safe = readEntry(entry)
    // Only reachable if the API breaks its own contract: show the answer, but don't keep what we couldn't read back.
    if (!safe) return entry
    update(tidy([safe, ...fresh().entries]))
    return safe
  },

  remove(id: string) {
    const entries = fresh().entries
    if (!entries.some((e) => e.id === id)) return
    update(entries.filter((e) => e.id !== id))
  },

  clear() {
    update([])
  },

  /** Tests only: forget the cached state so the next read goes back to storage. */
  resetForTests() {
    state = null
  },
}

/** Short, URL-safe and available on plain-http origins too (crypto.randomUUID needs a secure context). */
function newId(): string {
  const bytes = new Uint8Array(8)
  crypto.getRandomValues(bytes)
  return Array.from(bytes, (b) => (b % 36).toString(36)).join("") + Date.now().toString(36).slice(-4)
}

export const ENTRY_ID_PATTERN = /^[a-z0-9]{6,32}$/

function queryKey(query: string): string {
  return query.normalize("NFC").trim().replace(/\s+/gu, " ").toLowerCase()
}

/* Readers: each returns a fresh copy holding only the allowlisted fields, or null when any field is wrong. */

function readEntry(value: unknown): HistoryEntry | null {
  if (!isObject(value)) return null
  const { id, query, savedAt } = value
  if (typeof id !== "string" || !ENTRY_ID_PATTERN.test(id)) return null
  if (typeof query !== "string" || query.trim().length === 0) return null
  if (typeof savedAt !== "string" || Number.isNaN(Date.parse(savedAt))) return null
  const result = readSnapshot(value.result)
  return result ? { id, query, savedAt, result } : null
}

function readSnapshot(value: unknown): ResultSnapshot | null {
  if (!isObject(value)) return null
  const { query, status, hint, degraded_reasons } = value
  if (typeof query !== "string" || !isNullableString(hint)) return null
  if (!(MATCH_STATUSES as readonly unknown[]).includes(status)) return null
  if (!Array.isArray(degraded_reasons) || !degraded_reasons.every(isDegradedReason)) return null
  if (!Array.isArray(value.candidates)) return null
  const candidates = value.candidates.map(readCandidate)
  if (!candidates.every((c) => c !== null)) return null
  // Entries saved before topic requests existed have no topics: they were book requests.
  const topics = value.topics === undefined ? [] : readStrings(value.topics)
  if (!topics) return null
  return {
    query,
    status: status as ResultSnapshot["status"],
    hint,
    degraded_reasons: [...degraded_reasons],
    topics,
    candidates,
  }
}

function readCandidate(value: unknown): CandidateSnapshot | null {
  if (!isObject(value)) return null
  const v = value
  if (!isCount(v.rank) || v.rank < 1) return null
  if (typeof v.work_id !== "string" || v.work_id.length === 0 || typeof v.title !== "string") return null
  if (!isNullableString(v.subtitle) || !isNullableString(v.author)) return null
  if (!Array.isArray(v.primary_authors) || !Array.isArray(v.contributors)) return null
  const primaryAuthors = v.primary_authors.map(readAuthor)
  const contributors = v.contributors.map(readContributor)
  if (!primaryAuthors.every((a) => a !== null) || !contributors.every((c) => c !== null)) return null
  if (!isCount(v.primary_authors_total) || !isCount(v.contributors_total) || !isCount(v.edition_count)) return null
  if (!(v.first_publish_year === null || Number.isInteger(v.first_publish_year))) return null
  if (typeof v.open_library_url !== "string" || !isNullableString(v.cover_url)) return null
  if (typeof v.explanation !== "string" || typeof v.second_opinion !== "boolean") return null
  const matchedSubjects = v.matched_subjects === undefined ? [] : readStrings(v.matched_subjects)
  if (!matchedSubjects) return null
  return {
    rank: v.rank,
    work_id: v.work_id,
    title: v.title,
    subtitle: v.subtitle,
    author: v.author,
    primary_authors: primaryAuthors,
    primary_authors_total: v.primary_authors_total,
    contributors,
    contributors_total: v.contributors_total,
    first_publish_year: v.first_publish_year as number | null,
    edition_count: v.edition_count,
    open_library_url: v.open_library_url,
    cover_url: v.cover_url,
    explanation: v.explanation,
    second_opinion: v.second_opinion,
    matched_subjects: matchedSubjects,
  }
}

function readStrings(value: unknown): string[] | null {
  return Array.isArray(value) && value.every((s) => typeof s === "string") ? [...value] : null
}

function readAuthor(value: unknown): AuthorLink | null {
  if (
    !isObject(value) ||
    typeof value.name !== "string" ||
    !isNullableString(value.key) ||
    !isNullableString(value.url)
  )
    return null
  return { key: value.key, name: value.name, url: value.url }
}

function readContributor(value: unknown): ContributorLink | null {
  if (
    !isObject(value) ||
    typeof value.name !== "string" ||
    !isNullableString(value.key) ||
    !isNullableString(value.role)
  )
    return null
  return { key: value.key, name: value.name, role: value.role }
}

function isDegradedReason(value: unknown): value is DegradedReason {
  return (DEGRADED_REASONS as readonly unknown[]).includes(value)
}

function isCount(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value >= 0
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === "string"
}
