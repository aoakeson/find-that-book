import { useId } from "react"
import { Link } from "@tanstack/react-router"
import { CircleAlertIcon, CircleStopIcon, HistoryIcon, InfoIcon, TriangleAlertIcon } from "lucide-react"
import type { ApiError } from "@/api/client"
import type { DegradedReason } from "@/api/types"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button, buttonVariants } from "@/components/ui/button"
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import { cn } from "@/lib/utils"
import { describeDegraded } from "./degraded"
import { describeFailure } from "./failure"
import { useCountdown } from "./use-clock"

/** A valid answer that took a simpler path. Quiet on purpose: the results below it are real. */
export function DegradedNotice({ reasons }: { reasons: readonly DegradedReason[] }) {
  const text = describeDegraded(reasons)
  if (!text) return null
  const warning = text.tone === "warning"
  const Icon = warning ? TriangleAlertIcon : InfoIcon
  return (
    <div
      role="note"
      className={cn(
        "flex gap-3 rounded-panel border px-4 py-3 text-sm text-ink",
        warning ? "border-warning-line bg-warning-surface" : "border-line bg-mist"
      )}
    >
      <Icon
        aria-hidden="true"
        className={cn("mt-0.5 size-4 shrink-0", warning ? "text-warning" : "text-ink-secondary")}
      />
      <div className="flex min-w-0 flex-col gap-1">
        {text.sentences.map((s) => (
          <p key={s}>{s}</p>
        ))}
      </div>
    </div>
  )
}

/**
 * A search that produced no answer. Kept next to the results area with an explicit retry; a server-requested wait
 * (Retry-After) keeps "Try again" disabled until it runs out. Nothing retries on its own. Not a live region: the
 * results announcer reads the title once.
 */
export function SearchFailure({
  error,
  retryAt,
  onRetry,
}: {
  error: ApiError
  retryAt: number | null
  onRetry: () => void
}) {
  const text = describeFailure(error)
  const remaining = useCountdown(retryAt)
  const titleId = useId()
  return (
    <Alert
      role="group"
      aria-labelledby={titleId}
      className="gap-1 border-danger-line bg-danger-surface px-4 py-3 text-ink"
    >
      <CircleAlertIcon aria-hidden="true" className="translate-y-1! text-danger" />
      <AlertTitle id={titleId} className="text-base font-semibold text-ink">
        {text.title}
      </AlertTitle>
      <AlertDescription className="flex flex-col items-start gap-3 text-ink">
        <p className="mb-0!">{text.description}</p>
        {text.reference && (
          <p className="mb-0! text-caption text-ink-secondary">
            Reference: <span className="font-mono break-all">{text.reference}</span>
          </p>
        )}
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          {/* Stays in the tab order while it waits, so the countdown beside it can be found. */}
          <Button onClick={onRetry} disabled={remaining > 0} focusableWhenDisabled>
            Try again
          </Button>
          {text.waitSeconds !== null && remaining > 0 && (
            <>
              <span aria-hidden="true" className="text-sm text-ink-secondary tabular-nums">
                You can try again in {remaining} s
              </span>
              <span className="sr-only">The server asked to wait {text.waitSeconds} seconds before trying again.</span>
            </>
          )}
        </div>
      </AlertDescription>
    </Alert>
  )
}

/** The user pressed Stop. Neutral: nothing went wrong. */
export function SearchStopped({ query, onRetry }: { query: string; onRetry: () => void }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-3 rounded-panel border border-line bg-surface px-4 py-3">
      <div className="flex min-w-0 items-start gap-3">
        <CircleStopIcon aria-hidden="true" className="mt-1 size-4 shrink-0 text-ink-secondary" />
        <div className="min-w-0">
          <p className="font-semibold text-ink">Search stopped</p>
          <p className="text-sm wrap-break-word text-ink-secondary">
            You stopped the search for “{query}” before it finished.
          </p>
        </div>
      </div>
      <Button variant="outline" onClick={onRetry}>
        Search again
      </Button>
    </div>
  )
}

/** ?entry= points at a snapshot this browser doesn't have (another device, deleted, cleared storage). */
export function MissingEntry() {
  return (
    <Empty className="border border-dashed border-line bg-surface/60">
      <EmptyHeader>
        <EmptyMedia
          variant="icon"
          className="size-10 rounded-lg bg-sage text-teal [&_svg:not([class*='size-'])]:size-5"
        >
          <HistoryIcon aria-hidden="true" />
        </EmptyMedia>
        <EmptyTitle>
          <h2 className="text-title font-semibold text-ink">This saved search isn't on this device</h2>
        </EmptyTitle>
        <EmptyDescription className="text-base text-ink-secondary">
          Search history is kept in this browser only, so a search saved elsewhere, deleted or cleared can't be opened
          here.
        </EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Link to="/" className={cn(buttonVariants({ variant: "outline" }))}>
          Back to search
        </Link>
      </EmptyContent>
    </Empty>
  )
}
