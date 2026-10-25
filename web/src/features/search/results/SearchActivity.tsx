import { CheckIcon, CircleCheckIcon, TriangleAlertIcon } from "lucide-react"
import type { SearchResponse } from "@/api/types"
import { PageTurnBook } from "@/app/PageTurnBook"
import {
  Trace,
  TraceCollapsible,
  TraceCollapsibleContent,
  TraceCollapsibleTrigger,
  TraceDescription,
  TraceIcon,
  TraceLine,
  TraceTitle,
} from "@/components/ui/trace"
import { formatDuration } from "@/lib/format"
import { cn } from "@/lib/utils"
import { describeActivity, type ActivityStep } from "./activity"
import { useElapsedSeconds } from "./use-clock"

/**
 * While a search runs. The API sends one answer at the end and no progress events, so this says only what is true
 * (searching, for how long) and never shows stages it can't know. The page-turn book is the only moving part.
 */
export function RunningActivity({ startedAt }: { startedAt: number }) {
  const seconds = useElapsedSeconds(startedAt)
  return (
    <Trace>
      <TraceLine className="min-h-6 pointer-coarse:min-h-11">
        <PageTurnBook active className="size-5 shrink-0 text-teal" />
        <TraceTitle className="text-ink">Searching…</TraceTitle>
        <TraceDescription aria-hidden="true" className="text-ink-secondary">
          {seconds} s
        </TraceDescription>
      </TraceLine>
    </Trace>
  )
}

/** After a search: a one-line summary that expands into the steps the response reports, with measured times. */
export function SearchActivity({
  response,
  open,
  onOpenChange,
}: {
  response: SearchResponse
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const activity = describeActivity(response)
  return (
    <Trace>
      <TraceCollapsible open={open} onOpenChange={(next) => onOpenChange(next)}>
        <TraceCollapsibleTrigger className="min-h-6 pointer-coarse:min-h-11">
          <TraceLine>
            <TraceIcon className="size-5 text-teal">
              <CircleCheckIcon />
            </TraceIcon>
            <TraceTitle>Search activity</TraceTitle>
            <TraceDescription className="text-ink-secondary">{formatDuration(activity.elapsedMs)}</TraceDescription>
          </TraceLine>
        </TraceCollapsibleTrigger>
        <TraceCollapsibleContent className="ml-[calc(--spacing(2.5)-1px)]">
          <ol className="flex flex-col gap-3">
            {activity.steps.map((step) => (
              <ActivityStepItem key={step.id} step={step} />
            ))}
          </ol>
          <div className="mt-4 flex flex-col gap-0.5 text-caption text-ink-secondary">
            {activity.footnotes.map((note) => (
              <p key={note} className="wrap-break-word">
                {note}
              </p>
            ))}
            {/* An opaque id with no natural break points: the only line allowed to break anywhere. */}
            <p className="break-all">Request ID: {activity.requestId}</p>
          </div>
        </TraceCollapsibleContent>
      </TraceCollapsible>
    </Trace>
  )
}

function ActivityStepItem({ step }: { step: ActivityStep }) {
  return (
    <li className="flex min-w-0 flex-col gap-1">
      <TraceLine className="text-ink">
        <TraceIcon className={step.fallback ? "text-warning" : "text-teal"}>
          {step.fallback ? <TriangleAlertIcon /> : <CheckIcon />}
        </TraceIcon>
        <TraceTitle className="font-medium">{step.title}</TraceTitle>
        {step.durationMs !== null && (
          <TraceDescription className="text-ink-secondary">{formatDuration(step.durationMs)}</TraceDescription>
        )}
      </TraceLine>
      {step.note && <p className="pl-6 text-sm text-ink-secondary">{step.note}</p>}
      {step.details.length > 0 && (
        <ul className="flex flex-col gap-1 pl-6 text-sm text-ink-secondary">
          {step.details.map((d, i) => (
            <li key={i} className="flex min-w-0 flex-wrap items-baseline gap-x-2">
              {d.warning && (
                <TriangleAlertIcon aria-hidden="true" className="size-3.5 shrink-0 translate-y-0.5 text-warning" />
              )}
              <span className="min-w-0 wrap-break-word">{d.text}</span>
              {d.description && (
                <span className={cn("tabular-nums", d.warning ? "text-warning" : "text-ink-secondary")}>
                  {d.description}
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
    </li>
  )
}
