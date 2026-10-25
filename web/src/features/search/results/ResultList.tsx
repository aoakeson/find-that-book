import { useId } from "react"
import { SearchXIcon } from "lucide-react"
import type { ResultSnapshot } from "@/features/history/snapshot"
import { Button } from "@/components/ui/button"
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import { formatList } from "@/lib/format"
import { Candidate } from "./Candidate"

/** The answer in the server's order and status. Nothing is re-ranked, scored or hidden here. */
export function ResultList({ snapshot, onEditSearch }: { snapshot: ResultSnapshot; onEditSearch: () => void }) {
  const newTabNoteId = useId()
  const headingId = useId()
  const othersId = useId()
  const [first, ...rest] = snapshot.candidates

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
              {rest.map((c) => (
                <li key={c.work_id} className="py-5 last:pb-0">
                  <Candidate candidate={c} variant="other" newTabNoteId={newTabNoteId} />
                </li>
              ))}
            </ul>
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
        {snapshot.candidates.map((c) => (
          <li key={c.work_id} className="p-4 sm:p-5">
            <Candidate candidate={c} variant="equal" newTabNoteId={newTabNoteId} />
          </li>
        ))}
      </ol>
      {note}
    </section>
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
