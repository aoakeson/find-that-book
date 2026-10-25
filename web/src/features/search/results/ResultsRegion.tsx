import { useId, type ReactNode } from "react"
import { useSearchSession, type SearchSession } from "@/features/search/session"
import { Button } from "@/components/ui/button"
import { formatDateTime, formatRelativeTime } from "@/lib/format"
import { Announcer } from "./Announcer"
import { announcementFor } from "./announcement"
import { DegradedNotice, MissingEntry, SearchFailure, SearchStopped } from "./Notices"
import { ResultList } from "./ResultList"
import { ResultsSkeleton } from "./ResultsSkeleton"
import { RunningActivity, SearchActivity } from "./SearchActivity"

/** The slice of the search session the results area uses (a stub in tests). */
export type ResultsSession = Pick<
  SearchSession,
  "view" | "retry" | "focusComposer" | "activityOpen" | "setActivityOpen"
>

/** Everything under the compact composer: the running search, its answer, or why there isn't one. */
export function ResultsRegion() {
  return <ResultsRegionView session={useSearchSession()} />
}

export function ResultsRegionView({ session }: { session: ResultsSession }) {
  const { view } = session
  return (
    <div className="mt-6 flex min-w-0 flex-col gap-6 sm:mt-8">
      <Announcer announcement={announcementFor(view)} />
      <RegionBody session={session} />
    </div>
  )
}

function RegionBody({ session }: { session: ResultsSession }) {
  const { view, focusComposer, activityOpen, setActivityOpen } = session
  const retry = () => retryKeepingFocus(session.retry)
  switch (view.kind) {
    case "home":
      return null
    case "pending":
      return (
        <ResultsFor query={view.query}>
          <RunningActivity key={view.runId} startedAt={view.startedAt} />
          <ResultsSkeleton />
        </ResultsFor>
      )
    case "success":
      return (
        <ResultsFor query={view.query}>
          <SearchActivity response={view.response} open={activityOpen} onOpenChange={setActivityOpen} />
          <DegradedNotice reasons={view.snapshot.degraded_reasons} />
          <ResultList snapshot={view.snapshot} onEditSearch={focusComposer} />
        </ResultsFor>
      )
    case "restored":
      return (
        <ResultsFor
          query={view.entry.query}
          aside={
            <>
              <p className="text-sm text-ink-secondary">
                Saved{" "}
                <time dateTime={view.entry.savedAt} title={formatDateTime(view.entry.savedAt)}>
                  {formatRelativeTime(view.entry.savedAt)}
                </time>
                <span className="sr-only"> ({formatDateTime(view.entry.savedAt)})</span>
              </p>
              <Button variant="outline" size="sm" onClick={retry}>
                Search again
              </Button>
            </>
          }
        >
          <DegradedNotice reasons={view.entry.result.degraded_reasons} />
          <ResultList snapshot={view.entry.result} onEditSearch={focusComposer} />
        </ResultsFor>
      )
    case "failed":
      return (
        <>
          <SearchFailure key={view.runId} error={view.error} retryAt={view.retryAt} onRetry={retry} />
          {view.previous && (
            <PreviousResults query={view.previous.query}>
              <ResultList snapshot={view.previous.snapshot} onEditSearch={focusComposer} />
            </PreviousResults>
          )}
        </>
      )
    case "cancelled":
      return <SearchStopped query={view.query} onRetry={retry} />
    case "missing-entry":
      return <MissingEntry />
  }
}

/**
 * "Try again" and "Search again" are replaced by the search they start, which would drop keyboard focus to the top of
 * the page. Hand it to the composer's Stop button instead (Search, if the answer is already back). A button, not the
 * field, so a phone's keyboard doesn't pop up.
 */
function retryKeepingFocus(retry: () => void) {
  retry()
  requestAnimationFrame(() => {
    const active = document.activeElement
    if (active && active !== document.body) return
    document.querySelector<HTMLElement>("[data-composer-action]")?.focus()
  })
}

/** Content kept from before a failed search: clearly labelled, so it can't pass for the new search's answer. */
function PreviousResults({ query, children }: { query: string; children: ReactNode }) {
  const labelId = useId()
  return (
    <section aria-labelledby={labelId} className="flex min-w-0 flex-col gap-4 border-t border-line pt-6">
      <p id={labelId} className="min-w-0 wrap-break-word text-ink-secondary">
        Previous results for <span className="font-medium text-ink">“{query}”</span>
      </p>
      {children}
    </section>
  )
}

/** Labels the answer with the query it belongs to, so a result can never pass for another search's. */
function ResultsFor({ query, aside, children }: { query: string; aside?: ReactNode; children: ReactNode }) {
  const labelId = useId()
  return (
    <section aria-labelledby={labelId} className="flex min-w-0 flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <p id={labelId} className="min-w-0 wrap-break-word text-ink-secondary">
          Results for <span className="font-medium text-ink">“{query}”</span>
        </p>
        {aside && <div className="flex flex-wrap items-center gap-x-3 gap-y-2">{aside}</div>}
      </div>
      {children}
    </section>
  )
}
