import { CogIcon, PauseIcon, PlayIcon, RotateCcwIcon, SparklesIcon } from "lucide-react"
import type { ReactNode } from "react"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import type { Sequence } from "./use-sequence"

/**
 * One illustration: a stage track (each stage is a button that jumps there), the drawing, and a caption with
 * Pause / Play / Replay. All of the drawing's text is in the DOM from the start, so assistive technology reads the
 * complete picture whatever step is on screen.
 */
export function Figure({
  sequence,
  stages,
  caption,
  label,
  toolbar,
  children,
  className,
}: {
  sequence: Sequence
  /** Short names for the steps, in order; one per step of the sequence. */
  stages: string[]
  caption: ReactNode
  /** Accessible name for the stage list, e.g. "Cleaning stages". */
  label: string
  /** Optional controls above the drawing (an example picker). */
  toolbar?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <figure className={cn("overflow-hidden rounded-panel border border-line bg-surface shadow-panel", className)}>
      {toolbar && <div className="border-b border-line px-4 py-3 sm:px-5">{toolbar}</div>}
      <StageTrack sequence={sequence} stages={stages} label={label} />
      <Drawing attach={sequence.attach}>{children}</Drawing>
      <figcaption className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t border-line bg-paper px-4 py-3 text-sm text-ink-secondary sm:px-5">
        <span className="min-w-0 flex-1 basis-60 text-pretty">{caption}</span>
        {sequence.animated && <PlayControl sequence={sequence} />}
      </figcaption>
    </figure>
  )
}

function Drawing({ attach, children }: { attach: Sequence["attach"]; children: ReactNode }) {
  return (
    <div ref={attach} className="px-4 pt-2 pb-6 sm:px-6">
      {children}
    </div>
  )
}

function StageTrack({ sequence, stages, label }: { sequence: Sequence; stages: string[]; label: string }) {
  return (
    <ol aria-label={label} className="flex flex-wrap gap-x-1 gap-y-1 px-3 pt-3 sm:px-4">
      {stages.map((stage, i) => {
        const state = i < sequence.step ? "done" : i === sequence.step ? "current" : "next"
        return (
          <li key={`${i}-${stage}`}>
            <button
              type="button"
              onClick={() => sequence.goTo(i)}
              aria-current={state === "current" ? "step" : undefined}
              className={cn(
                "inline-flex min-h-9 items-center gap-2 rounded-lg px-2.5 text-sm transition-colors duration-200 ease-(--ease-out-soft) pointer-coarse:min-h-11",
                state === "current" && "bg-sage font-medium text-ink",
                state === "done" && "text-ink hover:bg-muted",
                state === "next" && "text-ink-secondary hover:bg-muted hover:text-ink"
              )}
            >
              <span
                aria-hidden="true"
                className={cn(
                  "flex size-5 items-center justify-center rounded-full text-caption font-semibold tabular-nums transition-colors duration-200",
                  state === "next" ? "bg-mist text-ink-secondary" : "bg-teal text-white"
                )}
              >
                {i + 1}
              </span>
              {stage}
            </button>
          </li>
        )
      })}
    </ol>
  )
}

function PlayControl({ sequence }: { sequence: Sequence }) {
  const { status } = sequence
  const [icon, text] =
    status === "playing"
      ? [<PauseIcon key="p" />, "Pause"]
      : status === "done"
        ? [<RotateCcwIcon key="r" />, "Replay"]
        : [<PlayIcon key="s" />, "Play"]
  return (
    <Button variant="outline" size="sm" onClick={sequence.toggle} aria-label={`${text} animation`} className="shrink-0">
      {icon}
      {text}
    </Button>
  )
}

export interface PickerOption {
  id: string
  label: string
}

/** A row of example chips; picking one replays the illustration with it. */
export function ExamplePicker({
  label,
  options,
  value,
  onChange,
}: {
  label: string
  options: PickerOption[]
  value: string
  onChange: (id: string) => void
}) {
  return (
    <div role="group" aria-label={label} className="flex flex-wrap items-center gap-1.5">
      <span aria-hidden="true" className="mr-1 text-sm text-ink-secondary">
        {label}
      </span>
      {options.map((o) => (
        <Button
          key={o.id}
          variant="outline"
          size="sm"
          aria-pressed={o.id === value}
          onClick={() => onChange(o.id)}
          className="aria-pressed:border-teal aria-pressed:bg-sage aria-pressed:text-ink"
        >
          {o.label}
        </Button>
      ))}
    </div>
  )
}

/** Who does a step: the AI model, or plain deterministic code. Colour plus icon plus word, never colour alone. */
export function Actor({ kind, className }: { kind: "ai" | "code"; className?: string }) {
  return kind === "ai" ? (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full bg-ai-surface px-2 py-0.5 text-caption font-medium text-ai",
        className
      )}
    >
      <SparklesIcon aria-hidden="true" className="size-3.5" />
      AI
    </span>
  ) : (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full bg-sage px-2 py-0.5 text-caption font-medium text-teal",
        className
      )}
    >
      <CogIcon aria-hidden="true" className="size-3.5" />
      Code
    </span>
  )
}

/** Reveal helper: visible from step `at` on, sliding up a little as it appears. */
export function reveal(step: number, at: number, extra?: string) {
  return cn(
    "transition-[opacity,translate] duration-500 ease-(--ease-out-soft)",
    step >= at ? "translate-y-0 opacity-100" : "translate-y-1.5 opacity-0",
    extra
  )
}
