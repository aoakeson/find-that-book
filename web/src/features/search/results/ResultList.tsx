import { useId, useRef, useState } from "react"
import { flushSync } from "react-dom"
import { SearchXIcon } from "lucide-react"
import type { CandidateSnapshot, ResultSnapshot } from "@/features/history/snapshot"
import { Button } from "@/components/ui/button"
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import { formatList } from "@/lib/format"
import { cn } from "@/lib/utils"
import { Candidate } from "./Candidate"

/** How many books show before "Show more": the brief's "up to 5 ordered candidates". */
export const INITIALLY_SHOWN = 5

/**
 * The answer in the server's order and status. Nothing is re-ranked or scored; books past the first five wait behind
 * "Show more", in the same order, and are already part of this answer (no new request).
 */
export function ResultList({ snapshot, onEditSearch }: { snapshot: ResultSnapshot; onEditSearch: () => void }) {
  const newTabNoteId = useId()
  const headingId = useId()
  const othersId = useId()
  const [expandedFor, setExpandedFor] = useState<ResultSnapshot | null>(null)
  const firstRevealedRef = useRef<HTMLHeadingElement>(null)

  const all = snapshot.candidates
  const initial = initialCount(all)
  const expanded = expandedFor === snapshot
  const visible = expanded ? all : all.slice(0, initial)
  const hidden = all.length - visible.length
  const titleRef = (index: number) => (expanded && index === initial ? firstRevealedRef : undefined)
  const showMore = () => {
    flushSync(() => setExpandedFor(snapshot))
    firstRevealedRef.current?.focus()
  }
  const showMoreButton = (className?: string) =>
    hidden > 0 && <ShowMoreButton count={hidden} onClick={showMore} className={className} />

  const [first, ...rest] = visible

  const topics = snapshot.topics.length > 0 ? formatList(snapshot.topics) : null
  if (snapshot.status === "none" || !first)
    return <NoMatch topics={topics} hint={snapshot.hint} onEditSearch={onEditSearch} />

  const note = (
    <span id={newTabNoteId} hidden>
      Opens in a new tab
    </span>
  )

  // A topic request ("a book about bears") is a list of fitting books, never one best match.
  if (snapshot.status === "match" && !topics) {
    return (
      <div className="flex flex-col gap-8">
        <section aria-labelledby={headingId} className="flex flex-col gap-3">
          <h2 id={headingId} className="text-base font-semibold text-ink">
            Best match
          </h2>
          <ul role="list">
            <li className="rounded-panel border border-line bg-surface p-4 shadow-panel sm:p-6">
              <Candidate candidate={first} variant="best" newTabNoteId={newTabNoteId} />
            </li>
          </ul>
        </section>
        {rest.length > 0 && (
          <section aria-labelledby={othersId} className="flex flex-col gap-1">
            <h2 id={othersId} className="text-base font-semibold text-ink">
              Other possibilities
            </h2>
            <ul role="list" className="divide-y divide-line">
              {rest.map((c, i) => (
                <li key={c.work_id} className="py-5 last:pb-0">
                  <Candidate candidate={c} variant="other" newTabNoteId={newTabNoteId} titleRef={titleRef(i + 1)} />
                </li>
              ))}
            </ul>
            {showMoreButton("mt-4")}
          </section>
        )}
        {note}
      </div>
    )
  }

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <h2 id={headingId} className="text-base font-semibold wrap-break-word text-ink">
          {topics ? `Books about ${topics}` : "A few books fit your clues"}
        </h2>
        {snapshot.hint && <p className="wrap-break-word text-ink-secondary">{snapshot.hint}</p>}
      </div>
      <ol role="list" className="divide-y divide-line rounded-panel border border-line bg-surface">
        {visible.map((c, i) => (
          <li key={c.work_id} className="p-4 sm:p-5">
            <Candidate candidate={c} variant="equal" newTabNoteId={newTabNoteId} titleRef={titleRef(i)} />
          </li>
        ))}
      </ol>
      {showMoreButton()}
      {note}
    </section>
  )
}

/** The judge's pick (`second_opinion`) is a book the server chose to show, so it is never behind the button. */
function initialCount(candidates: CandidateSnapshot[]): number {
  const pick = candidates.findIndex((c) => c.second_opinion)
  return Math.max(INITIALLY_SHOWN, pick + 1)
}

function ShowMoreButton({ count, onClick, className }: { count: number; onClick: () => void; className?: string }) {
  return (
    <Button variant="outline" onClick={onClick} className={cn("self-start", className)}>
      Show {count} more {count === 1 ? "book" : "books"}
    </Button>
  )
}

function NoMatch({
  topics,
  hint,
  onEditSearch,
}: {
  topics: string | null
  hint: string | null
  onEditSearch: () => void
}) {
  return (
    <Empty className="border border-dashed border-line bg-surface/60">
      <EmptyHeader>
        <EmptyMedia
          variant="icon"
          className="size-10 rounded-lg bg-sage text-teal [&_svg:not([class*='size-'])]:size-5"
        >
          <SearchXIcon aria-hidden="true" />
        </EmptyMedia>
        <EmptyTitle>
          <h2 className="text-title font-semibold wrap-break-word text-ink">
            {topics ? `No books found about ${topics}` : "No book matched"}
          </h2>
        </EmptyTitle>
        <EmptyDescription className="text-base text-ink-secondary">
          {hint ?? "Check the spelling, or add an author or another detail."}
        </EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button variant="outline" onClick={onEditSearch}>
          Edit your search
        </Button>
      </EmptyContent>
    </Empty>
  )
}
