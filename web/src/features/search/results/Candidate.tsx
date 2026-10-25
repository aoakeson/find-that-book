import type { Ref } from "react"
import { ExternalLinkIcon, ScaleIcon } from "lucide-react"
import type { CandidateSnapshot } from "@/features/history/snapshot"
import { Badge } from "@/components/ui/badge"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import { formatCount, safeExternalUrl } from "@/lib/format"
import { cn } from "@/lib/utils"
import { BookCover, type CoverSize } from "./BookCover"

/** best: the emphasised match panel. equal: an ambiguous contender. other: a quieter alternative under a match. */
export type CandidateVariant = "best" | "equal" | "other"

const COVER: Record<CandidateVariant, CoverSize> = { best: "lg", equal: "md", other: "sm" }

const GRID: Record<CandidateVariant, string> = {
  best: "grid-cols-[5rem_minmax(0,1fr)] sm:grid-cols-[7rem_minmax(0,1fr)] gap-x-4 sm:gap-x-6",
  equal: "grid-cols-[4rem_minmax(0,1fr)] sm:grid-cols-[5rem_minmax(0,1fr)] gap-x-4 sm:gap-x-5",
  other: "grid-cols-[3rem_minmax(0,1fr)] sm:grid-cols-[3.5rem_minmax(0,1fr)] gap-x-4",
}

/** Roles that would only repeat the "Also credited" label. */
const GENERIC_ROLES = new Set(["also credited", "contributor", "other contributor"])

const SECOND_OPINION_TEXT = "A second AI check picked this book instead of the first result."

/**
 * One book as the server returned it: required fields first (title, primary authors, year, explanation, link),
 * contributors as quieter credits. Nothing here is inferred: a missing year or author stays missing.
 */
export function Candidate({
  candidate,
  variant,
  newTabNoteId,
  titleRef,
}: {
  candidate: CandidateSnapshot
  variant: CandidateVariant
  newTabNoteId: string
  /** Makes the title a programmatic focus target (the first book "Show more" reveals). */
  titleRef?: Ref<HTMLHeadingElement>
}) {
  const c = candidate
  const href = safeExternalUrl(c.open_library_url)
  const facts = [
    c.first_publish_year !== null ? `First published ${c.first_publish_year}` : null,
    editions(c.edition_count),
  ].filter(Boolean)
  const wideBody = variant !== "other"

  return (
    <div className={cn("grid gap-y-3", GRID[variant])}>
      <BookCover
        url={c.cover_url}
        title={c.title}
        size={COVER[variant]}
        className={wideBody ? "sm:row-span-2" : "row-span-2"}
      />

      <div className="flex min-w-0 flex-col gap-1">
        <h3
          ref={titleRef}
          tabIndex={titleRef ? -1 : undefined}
          className="text-title font-semibold wrap-break-word text-ink"
        >
          {c.title}
        </h3>
        {c.subtitle && <p className="text-sm wrap-break-word text-ink-secondary">{c.subtitle}</p>}
        <p className="wrap-break-word text-ink">
          {c.author ? (
            <>
              by <span className="font-medium">{c.author}</span>
            </>
          ) : (
            <span className="text-ink-secondary">Author not listed</span>
          )}
        </p>
        {facts.length > 0 && <p className="text-sm text-ink-secondary">{facts.join(", ")}</p>}
        {c.second_opinion && <SecondOpinionBadge />}
      </div>

      <div
        className={cn(
          "flex max-w-measure min-w-0 flex-col items-start gap-2",
          wideBody ? "col-span-2 sm:col-span-1 sm:col-start-2" : "col-start-2"
        )}
      >
        <p className="wrap-break-word text-ink">{c.explanation}</p>
        {c.matched_subjects.length > 0 && <MatchedSubjects subjects={c.matched_subjects} />}
        <Credits candidate={c} />
        {href && (
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={openLabel(c)}
            aria-describedby={newTabNoteId}
            className="-my-2 inline-flex items-center gap-1.5 rounded-sm py-2 text-sm font-medium text-teal underline-offset-4 hover:text-teal-hover hover:underline pointer-coarse:-my-3 pointer-coarse:py-3"
          >
            Open Library
            <ExternalLinkIcon aria-hidden="true" className="size-3.5" />
          </a>
        )}
      </div>
    </div>
  )
}

/** Topic requests: the Open Library subject headings that matched, exactly as listed there. */
function MatchedSubjects({ subjects }: { subjects: string[] }) {
  return (
    <ul aria-label="Matching Open Library subjects" className="flex flex-wrap gap-1.5">
      {subjects.map((subject) => (
        <li key={subject}>
          <Badge variant="secondary" className="h-auto max-w-full bg-mist py-0.5 whitespace-normal text-ink">
            {subject}
          </Badge>
        </li>
      ))}
    </ul>
  )
}

function Credits({ candidate }: { candidate: CandidateSnapshot }) {
  const { contributors, contributors_total } = candidate
  if (contributors.length === 0) return null
  const names = contributors
    .map((p) => (p.role && !GENERIC_ROLES.has(p.role.toLowerCase()) ? `${p.name} (${p.role})` : p.name))
    .join(", ")
  const more = contributors_total - contributors.length
  return (
    <p className="text-sm wrap-break-word text-ink-secondary">
      Also credited: {names}
      {more > 0 && ` and ${formatCount(more)} more`}
    </p>
  )
}

function SecondOpinionBadge() {
  return (
    <Tooltip>
      <TooltipTrigger render={<span tabIndex={0} />} className="mt-1 w-fit rounded-4xl">
        <Badge variant="secondary" className="bg-sage text-ink">
          <ScaleIcon aria-hidden="true" />
          Second opinion
          <span className="sr-only">: {SECOND_OPINION_TEXT}</span>
        </Badge>
      </TooltipTrigger>
      <TooltipContent>{SECOND_OPINION_TEXT}</TooltipContent>
    </Tooltip>
  )
}

/** Author and year tell apart two results with the same title (they are often different works or editions). */
function openLabel(c: CandidateSnapshot): string {
  const about = [c.author, c.first_publish_year?.toString()].filter(Boolean).join(", ")
  return about ? `Open “${c.title}” (${about}) on Open Library` : `Open “${c.title}” on Open Library`
}

function editions(count: number): string | null {
  if (count <= 0) return null
  return `${formatCount(count)} ${count === 1 ? "edition" : "editions"}`
}
