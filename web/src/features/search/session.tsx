import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from "react"
import { useMutation } from "@tanstack/react-query"
import { useRouter } from "@tanstack/react-router"
import { ApiError } from "@/api/client"
import { searchBooks } from "@/api/endpoints"
import type { SearchResponse } from "@/api/types"
import { toSnapshot, type ResultSnapshot } from "@/features/history/snapshot"
import { historyStore, type HistoryEntry } from "@/features/history/storage"

/**
 * What the search page shows. Exactly one of these at a time, and every result view carries the query it belongs
 * to, so an answer can never be shown under a different search.
 */
export type SearchView =
  | { kind: "home" }
  | { kind: "pending"; runId: number; query: string; startedAt: number }
  | {
      kind: "success"
      runId: number
      query: string
      response: SearchResponse
      snapshot: ResultSnapshot
      /** The history entry it was saved as (kept in memory when the browser can't store it). */
      entryId: string
    }
  | {
      kind: "failed"
      runId: number
      query: string
      error: ApiError
      /** When the API's Retry-After ends (epoch ms), or null. One deadline for the button and new searches alike. */
      retryAt: number | null
      /** The answer that was on screen before this search, kept below the error and labelled as previous. */
      previous: { query: string; snapshot: ResultSnapshot } | null
    }
  | { kind: "cancelled"; runId: number; query: string }
  | { kind: "restored"; entry: HistoryEntry }
  | { kind: "missing-entry"; entryId: string }

export interface SearchSession {
  /** The composer text. Separate from the submitted query: editing it never changes what is on screen. */
  draft: string
  setDraft: (value: string) => void
  /** Inline message for the composer (empty input, or the API's 400 for the query). */
  fieldError: string | null
  /**
   * Set when a search was refused locally because the API's Retry-After (429, 503, 504) hasn't passed yet: the time
   * searching can resume. Not an input error; the composer shows a neutral countdown.
   */
  pausedUntil: number | null
  view: SearchView
  /** Runs a search for the given text, or the draft. A search already running for other text is cancelled. */
  submit: (query?: string) => void
  cancel: () => void
  /** Explicitly re-runs the current view's query (failed, stopped or restored). Never called automatically. */
  retry: () => void
  /** "New search": back to the empty home state. */
  reset: () => void
  /**
   * The search route reports ?entry= whenever the page renders it. A changed value (back/forward, refresh, a link)
   * shows that saved entry, or home for none; the unchanged value (returning from Admin) keeps what is on screen.
   */
  syncEntry: (entryId: string | undefined) => void
  /** History's pick: always shows the entry and stops a running search, even when the URL already names it. */
  restoreEntry: (entryId: string) => void
  activityOpen: boolean
  setActivityOpen: (open: boolean) => void
  /** Callback ref for the composer's input, so other parts of the page can move focus to it. */
  registerComposer: (input: HTMLInputElement | null) => void
  focusComposer: () => void
}

const SearchSessionContext = createContext<SearchSession | null>(null)

const ACTIVITY_OPEN_KEY = "ftb.activityOpen"
const FIELD_ERROR_CODES = new Set(["validation_failed", "no_searchable_terms"])
const EMPTY_QUERY_MESSAGE = "Type a title, an author, or any detail you remember."

interface Run {
  id: number
  query: string
  controller: AbortController
  /** Restored if the API rejects the text itself (400): an invalid query must not wipe what was on screen. */
  previous: SearchView
}

interface SearchVariables {
  query: string
  signal: AbortSignal
}

export function SearchSessionProvider({ children }: { children: ReactNode }) {
  const router = useRouter()
  const [draft, setDraftState] = useState("")
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [pausedUntil, setPausedUntil] = useState<number | null>(null)
  const [view, setViewState] = useState<SearchView>({ kind: "home" })
  const [activityOpen, setActivityOpenState] = useState(readActivityOpen)
  const viewRef = useRef(view)
  const runRef = useRef<Run | null>(null)
  const nextRunId = useRef(1)
  const composerRef = useRef<HTMLInputElement | null>(null)
  /** An answer that arrived while the user was on another page, so its ?entry= was never pushed. */
  const unsyncedEntry = useRef<string | null>(null)
  /** The ?entry= value the search page last showed (null before its first render). Only a change is navigation. */
  const lastSeenEntry = useRef<{ id: string | undefined } | null>(null)
  /** The API's Retry-After from the last failure (429, 503, 504): no new search before this time, from any button. */
  const waitUntil = useRef(0)

  // A mutation, not a query: a paid search must never re-run on focus, reconnect, remount or StrictMode re-renders.
  const { mutate } = useMutation<SearchResponse, ApiError, SearchVariables>({
    mutationKey: ["search"],
    mutationFn: ({ query, signal }) => searchBooks(query, signal),
    retry: false,
    // Offline: fail now with a manual retry, instead of pausing and starting the paid call on reconnect.
    networkMode: "always",
  })

  const setView = useCallback((next: SearchView) => {
    viewRef.current = next
    setViewState(next)
  }, [])

  const setDraft = useCallback((value: string) => {
    setDraftState(value)
    setFieldError(null)
  }, [])

  const abortRun = useCallback(() => {
    runRef.current?.controller.abort()
    runRef.current = null
  }, [])

  const submit = useCallback(
    (text?: string) => {
      // A retry passes the earlier query; the draft (which the user may have edited since) is left alone.
      const query = (text ?? draft).trim()
      if (query.length === 0) {
        setFieldError(EMPTY_QUERY_MESSAGE)
        return
      }
      if (waitUntil.current > Date.now()) {
        setFieldError(null)
        setPausedUntil(waitUntil.current)
        return
      }
      setPausedUntil(null)
      const running = runRef.current
      if (running && running.query === query) return // a double Enter or click: the same search is already running
      const previous = running ? running.previous : viewRef.current
      abortRun()

      const run: Run = { id: nextRunId.current++, query, controller: new AbortController(), previous }
      runRef.current = run
      unsyncedEntry.current = null
      setFieldError(null)
      setView({ kind: "pending", runId: run.id, query, startedAt: Date.now() })

      mutate(
        { query, signal: run.controller.signal },
        {
          onSuccess: (response) => {
            if (runRef.current?.id !== run.id) return
            runRef.current = null
            const snapshot = toSnapshot(response)
            const entry = historyStore.add(response.query || query, snapshot)
            setView({ kind: "success", runId: run.id, query, response, snapshot, entryId: entry.id })
            // Only move the URL when the user is still on the search page (they may have opened Admin meanwhile).
            if (router.state.location.pathname === "/") {
              void router.navigate({ to: "/", search: { entry: entry.id } })
            } else {
              unsyncedEntry.current = entry.id
            }
          },
          onError: (error) => {
            if (runRef.current?.id !== run.id) return
            runRef.current = null
            const apiError = error instanceof ApiError ? error : new ApiError("unexpected", String(error))
            if (apiError.kind === "aborted") return
            if (apiError.status === 400 && apiError.code !== null && FIELD_ERROR_CODES.has(apiError.code)) {
              setFieldError(
                apiError.fieldErrors("Q")[0] ??
                  apiError.problem?.hint ??
                  apiError.problem?.detail ??
                  "Check the search text and try again."
              )
              setView(run.previous)
              return
            }
            const retryAt = apiError.retryAfterSeconds ? Date.now() + apiError.retryAfterSeconds * 1000 : null
            if (retryAt) waitUntil.current = retryAt
            setView({
              kind: "failed",
              runId: run.id,
              query,
              error: apiError,
              retryAt,
              previous: answerOf(run.previous),
            })
          },
        }
      )
    },
    [abortRun, draft, mutate, router, setView]
  )

  const cancel = useCallback(() => {
    const run = runRef.current
    if (!run) return
    abortRun()
    setView({ kind: "cancelled", runId: run.id, query: run.query })
  }, [abortRun, setView])

  const retry = useCallback(() => {
    const v = viewRef.current
    if (v.kind === "failed" || v.kind === "cancelled") submit(v.query)
    else if (v.kind === "restored") submit(v.entry.query)
  }, [submit])

  const registerComposer = useCallback((input: HTMLInputElement | null) => {
    composerRef.current = input
  }, [])

  const focusComposer = useCallback(() => {
    // After the current render, so a just-mounted composer can take focus.
    requestAnimationFrame(() => composerRef.current?.focus())
  }, [])

  const reset = useCallback(() => {
    abortRun()
    unsyncedEntry.current = null
    setDraftState("")
    setFieldError(null)
    setView({ kind: "home" })
  }, [abortRun, setView])

  const showEntry = useCallback(
    (entryId: string | undefined, reload = false) => {
      const v = viewRef.current
      if (entryId !== undefined && entryId === unsyncedEntry.current) unsyncedEntry.current = null
      if (entryId !== undefined && entryId === currentEntryId(v) && !reload) return
      if (entryId === undefined) {
        // Back to the bare search page: drop a shown answer, but keep a search the user is still waiting for. If that
        // search is then rejected (400), it falls back to home, not to an answer from the URL the user just left.
        if (v.kind === "success" || v.kind === "restored" || v.kind === "missing-entry") setView({ kind: "home" })
        if (runRef.current) runRef.current.previous = { kind: "home" }
        return
      }
      // A saved answer replaces whatever was running: its late result must not overwrite the restored one.
      abortRun()
      setFieldError(null)
      const entry = historyStore.get(entryId)
      if (entry) {
        setDraftState(entry.query)
        setView({ kind: "restored", entry })
      } else {
        setView({ kind: "missing-entry", entryId })
      }
    },
    [abortRun, setView]
  )

  const syncEntry = useCallback(
    (entryId: string | undefined) => {
      const seen = lastSeenEntry.current
      lastSeenEntry.current = { id: entryId }
      if (seen === null || seen.id !== entryId) {
        showEntry(entryId)
        return
      }
      // The same URL as when the user left (e.g. browser Back from Admin): keep what is on screen, including a search
      // still running. An answer that arrived meanwhile was never put in the URL; record it now.
      const v = viewRef.current
      if (v.kind === "success" && v.entryId === unsyncedEntry.current) {
        unsyncedEntry.current = null
        void router.navigate({ to: "/", search: { entry: v.entryId }, replace: true })
      }
    },
    [router, showEntry]
  )

  const restoreEntry = useCallback(
    (entryId: string) => {
      lastSeenEntry.current = { id: entryId }
      // Reload even when this entry is on screen: another tab may have saved a newer answer under the same id.
      showEntry(entryId, true)
    },
    [showEntry]
  )

  const setActivityOpen = useCallback((open: boolean) => {
    setActivityOpenState(open)
    try {
      window.localStorage.setItem(ACTIVITY_OPEN_KEY, open ? "1" : "0")
    } catch {
      // A convenience only.
    }
  }, [])

  const value = useMemo<SearchSession>(
    () => ({
      draft,
      setDraft,
      fieldError,
      pausedUntil,
      view,
      submit,
      cancel,
      retry,
      reset,
      syncEntry,
      restoreEntry,
      activityOpen,
      setActivityOpen,
      registerComposer,
      focusComposer,
    }),
    [
      draft,
      setDraft,
      fieldError,
      pausedUntil,
      view,
      submit,
      cancel,
      retry,
      reset,
      syncEntry,
      restoreEntry,
      activityOpen,
      setActivityOpen,
      registerComposer,
      focusComposer,
    ]
  )

  return <SearchSessionContext.Provider value={value}>{children}</SearchSessionContext.Provider>
}

export function useSearchSession(): SearchSession {
  const session = useContext(SearchSessionContext)
  if (!session) throw new Error("useSearchSession must be used inside <SearchSessionProvider>")
  return session
}

/** The history entry a view is showing, if any. */
export function currentEntryId(view: SearchView): string | null {
  switch (view.kind) {
    case "success":
      return view.entryId
    case "restored":
      return view.entry.id
    case "missing-entry":
      return view.entryId
    default:
      return null
  }
}

/** The answer a view shows, if any (a failed view keeps the one before it). */
function answerOf(view: SearchView): { query: string; snapshot: ResultSnapshot } | null {
  switch (view.kind) {
    case "success":
      return { query: view.query, snapshot: view.snapshot }
    case "restored":
      return { query: view.entry.query, snapshot: view.entry.result }
    case "failed":
      return view.previous
    default:
      return null
  }
}

/** The query a view belongs to, for labels and the document title. */
export function viewQuery(view: SearchView): string | null {
  switch (view.kind) {
    case "home":
    case "missing-entry":
      return null
    case "restored":
      return view.entry.query
    default:
      return view.query
  }
}

function readActivityOpen(): boolean {
  try {
    return window.localStorage.getItem(ACTIVITY_OPEN_KEY) === "1"
  } catch {
    return false
  }
}
