import type { SearchView } from "@/features/search/session"
import type { ResultSnapshot } from "@/features/history/snapshot"
import { formatList } from "@/lib/format"
import { describeFailure } from "./failure"

export interface Announcement {
  /** Identity of the outcome: the same key is announced once, a new key again even with the same words. */
  key: string
  message: string
}

/** What the live region says for a view: one start message and one outcome per search, nothing in between. */
export function announcementFor(view: SearchView): Announcement | null {
  switch (view.kind) {
    case "home":
      return null
    case "pending":
      return { key: `run-${view.runId}-pending`, message: "Searching…" }
    case "success":
      return { key: `run-${view.runId}-done`, message: outcomeMessage(view.snapshot) }
    case "failed":
      return { key: `run-${view.runId}-failed`, message: describeFailure(view.error).title }
    case "cancelled":
      return { key: `run-${view.runId}-cancelled`, message: "Search stopped" }
    case "restored":
      return {
        key: `entry-${view.entry.id}-${showing(view)}`,
        message: `Showing a saved search. ${outcomeMessage(view.entry.result)}`,
      }
    case "missing-entry":
      return { key: `missing-${view.entryId}-${showing(view)}`, message: "This saved search isn't on this device" }
  }
}

/**
 * Saved views have no run id. Opening the same entry again creates a new view (announced again), while a view the
 * session puts back unchanged keeps its key (and stays silent).
 */
const shown = new WeakMap<SearchView, number>()
let nextShown = 1
function showing(view: SearchView): number {
  let n = shown.get(view)
  if (n === undefined) {
    n = nextShown++
    shown.set(view, n)
  }
  return n
}

export function outcomeMessage(snapshot: ResultSnapshot): string {
  const [first] = snapshot.candidates
  const topics = snapshot.topics.length > 0 ? formatList(snapshot.topics) : null
  if (snapshot.status === "none" || !first) return topics ? `No books found about ${topics}` : "No book matched"
  if (topics) {
    const n = snapshot.candidates.length
    return n === 1 ? `1 book about ${topics}` : `${n} books about ${topics}`
  }
  if (snapshot.status === "match")
    return first.author ? `Best match: ${first.title} by ${first.author}` : `Best match: ${first.title}`
  const n = snapshot.candidates.length
  return n === 1 ? "1 book fits your clues" : `${n} books fit your clues`
}
