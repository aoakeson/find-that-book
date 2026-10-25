import { CircleCheckIcon, CircleDashedIcon, CircleXIcon } from "lucide-react"
import { useState } from "react"
import { cn } from "@/lib/utils"
import { DECISION_EXAMPLES, type Outcome } from "./examples"
import { ExamplePicker, Figure, reveal } from "./figure"
import { useSequence } from "./use-sequence"

const STAGES = ["Tier", "Safety rules", "Lead", "Answer"]
const DURATIONS = [1800, 1800, 1800, 3400]

const GATES = [
  {
    question: "Is the top book in tier A or B?",
    detail: "Only an exact title can be the one best match.",
  },
  {
    question: "Do the safety rules all stay quiet?",
    detail:
      "For example, they hold back a match when the title came only from the AI, when one lone word is just a character’s name, when the words name a whole series, when the record is tiny, or when another author has the same title.",
  },
  {
    question: "Is it clearly ahead?",
    detail: "It leads its tier by at least 0.5 points, and no lower-tier book outscores it by 0.5.",
  },
]

const OUTCOME: Record<Outcome, { label: string; className: string }> = {
  match: { label: "Best match", className: "bg-teal text-white" },
  ambiguous: {
    label: "A few books fit your clues",
    className: "bg-warning-surface text-warning ring-1 ring-warning-line",
  },
  none: { label: "No book matched", className: "bg-mist text-ink ring-1 ring-line-strong" },
}

export function DecisionFigure() {
  const [id, setId] = useState(DECISION_EXAMPLES[0].id)
  const example = DECISION_EXAMPLES.find((e) => e.id === id) ?? DECISION_EXAMPLES[0]
  const sequence = useSequence(STAGES.length, DURATIONS, id)
  const { step } = sequence

  return (
    <Figure
      sequence={sequence}
      stages={STAGES}
      label="Decision stages"
      caption="Showing a few books is always safe. Code only claims a single best match when all three answers are yes."
      toolbar={
        <ExamplePicker
          label="Try"
          options={DECISION_EXAMPLES.map((e) => ({ id: e.id, label: e.typed }))}
          value={id}
          onChange={setId}
        />
      }
    >
      <ol className="grid gap-2">
        {GATES.map((gate, i) => {
          const answered = step >= i
          const state = i < example.reached ? "yes" : i === example.reached ? "no" : "skipped"
          return (
            <li
              key={gate.question}
              className={cn(
                "flex gap-3 rounded-lg border p-3 transition-[border-color,background-color] duration-500",
                !answered && "border-line bg-surface",
                answered && state === "yes" && "border-teal/40 bg-sage/60",
                answered && state === "no" && "border-warning-line bg-warning-surface",
                answered && state === "skipped" && "border-dashed border-line-strong bg-paper"
              )}
            >
              <span className="mt-0.5 shrink-0">
                {!answered || state === "skipped" ? (
                  <CircleDashedIcon aria-hidden="true" className="size-5 text-ink-secondary" />
                ) : state === "yes" ? (
                  <CircleCheckIcon aria-hidden="true" className="size-5 text-teal" />
                ) : (
                  <CircleXIcon aria-hidden="true" className="size-5 text-warning" />
                )}
              </span>
              <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="flex flex-wrap items-baseline justify-between gap-x-3">
                  <span className="font-medium text-ink">{gate.question}</span>
                  <span
                    className={cn(
                      "text-sm font-medium transition-opacity duration-300",
                      answered ? "opacity-100" : "opacity-0"
                    )}
                  >
                    {state === "yes"
                      ? "Yes"
                      : state === "no"
                        ? i === 0
                          ? `No, tier ${example.tier}`
                          : "No"
                        : "Not needed"}
                    {state === "yes" && i === 0 ? `, tier ${example.tier}` : ""}
                  </span>
                </span>
                <span className="text-sm text-ink-secondary">{gate.detail}</span>
              </span>
            </li>
          )
        })}
      </ol>
      <div className={reveal(step, 3, "mt-4 flex flex-col gap-2 sm:flex-row sm:items-center sm:gap-4")}>
        <span
          className={cn(
            "w-fit shrink-0 rounded-full px-3 py-1 text-sm font-medium",
            OUTCOME[example.outcome].className
          )}
        >
          {OUTCOME[example.outcome].label}
        </span>
        <p className="text-pretty text-ink">{example.note}</p>
      </div>
    </Figure>
  )
}
