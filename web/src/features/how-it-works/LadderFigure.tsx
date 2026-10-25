import { CircleCheckIcon, CornerDownRightIcon } from "lucide-react"
import { useState } from "react"
import { cn } from "@/lib/utils"
import { LADDER_EXAMPLES, RUNGS } from "./examples"
import { ExamplePicker, Figure, reveal } from "./figure"
import { useSequence } from "./use-sequence"

/** Per call: the request going out, then the check. */
const DURATIONS = [1700, 2300, 1700, 2300, 1700, 2300, 1700, 2300, 1700, 2300]
const RUNG_SHORT: Record<string, string> = {
  broad: "Broad search",
  reading: "AI’s title",
  anyOf: "Any word",
  characters: "Characters",
  authorWorks: "Author’s works",
}

export function LadderFigure() {
  const [id, setId] = useState(LADDER_EXAMPLES[2].id)
  const example = LADDER_EXAMPLES.find((e) => e.id === id) ?? LADDER_EXAMPLES[0]
  // Two steps per call: the request goes out, then the ranking is checked.
  const stages = example.calls.flatMap((c) => [RUNG_SHORT[c.rung], "Check"])
  const sequence = useSequence(stages.length, DURATIONS, id)
  const { step } = sequence
  const finished = step === stages.length - 1

  return (
    <Figure
      sequence={sequence}
      stages={stages}
      label="Search steps"
      caption="Recorded with the AI off, so every extra search here was decided by code alone. At most 5 calls and 25 seconds per search."
      toolbar={
        <ExamplePicker
          label="Try"
          options={LADDER_EXAMPLES.map((e) => ({ id: e.id, label: e.typed }))}
          value={id}
          onChange={setId}
        />
      }
    >
      <div className="grid gap-6 md:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
        <ol aria-label="Possible searches, in order" className="flex flex-col gap-1.5">
          {RUNGS.map((rung) => {
            const index = example.calls.findIndex((c) => c.rung === rung.id)
            const ran = index >= 0 && step >= index * 2
            const skipped = finished && index < 0
            return (
              <li
                key={rung.id}
                className={cn(
                  "flex items-start gap-3 rounded-lg border px-3 py-2.5 transition-[border-color,background-color] duration-500",
                  ran
                    ? "border-teal/40 bg-sage"
                    : skipped
                      ? "border-dashed border-line-strong bg-paper"
                      : "border-line bg-surface"
                )}
              >
                <span
                  className={cn(
                    "mt-0.5 flex h-5 min-w-12 items-center justify-center rounded-full px-1.5 text-caption font-medium transition-colors duration-500",
                    ran ? "bg-teal text-white" : "bg-mist text-ink-secondary"
                  )}
                >
                  {ran ? `Call ${index + 1}` : skipped ? "Skipped" : "Waiting"}
                </span>
                <span className="flex min-w-0 flex-col">
                  <span className="text-sm font-medium text-ink">{rung.name}</span>
                  <span className="text-caption text-ink-secondary">{rung.when}</span>
                </span>
              </li>
            )
          })}
        </ol>

        <ol aria-label="What happened" className="flex flex-col gap-3">
          {example.calls.map((call, i) => (
            <li key={call.request} className={reveal(step, i * 2, "flex flex-col gap-2")}>
              <div className="rounded-lg border border-line bg-paper px-3 py-2.5">
                <p className="text-caption text-ink-secondary">Call {i + 1} to Open Library</p>
                <p className="font-mono text-sm font-medium wrap-break-word text-ink">{call.request}</p>
                <p className="text-sm text-ink-secondary">
                  {call.results} {call.results === 1 ? "book" : "books"} back
                </p>
              </div>
              <p className={reveal(step, i * 2 + 1, "flex items-start gap-2 pl-1 text-sm text-ink")}>
                {call.stop ? (
                  <CircleCheckIcon aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-teal" />
                ) : (
                  <CornerDownRightIcon aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-ink-secondary" />
                )}
                <span>
                  <span className="font-medium">{call.stop ? "Stop. " : "Climb. "}</span>
                  {call.after}
                </span>
              </p>
            </li>
          ))}
        </ol>
      </div>
    </Figure>
  )
}
