import { CircleCheckIcon, CircleXIcon, SparklesIcon } from "lucide-react"
import { useState } from "react"
import { cn } from "@/lib/utils"
import { HOBBIT_FACTS, SENTENCE_EXAMPLES, TEMPLATE_SENTENCE } from "./examples"
import { Actor, ExamplePicker, Figure, reveal } from "./figure"
import { useSequence } from "./use-sequence"

const STAGES = ["Facts", "AI writes", "Code checks", "Shown"]
const DURATIONS = [1600, 2600, 2600, 3400]

/** Words and spaces, with "!" split off so it can be flagged on its own. */
const pieces = (sentence: string) => sentence.split(/(\s+|!)/).filter((p) => p.length > 0)
const bare = (piece: string) => (piece === "!" ? "!" : piece.toLowerCase().replace(/[^\p{L}\p{N}]/gu, ""))

export function ExplainFigure() {
  const [id, setId] = useState(SENTENCE_EXAMPLES[0].id)
  const example = SENTENCE_EXAMPLES.find((e) => e.id === id) ?? SENTENCE_EXAMPLES[0]
  const sequence = useSequence(STAGES.length, DURATIONS, id)
  const { step } = sequence
  const passed = example.flagged.length === 0
  const words = pieces(example.sentence)
  let wordIndex = 0

  return (
    <Figure
      sequence={sequence}
      stages={STAGES}
      label="Explanation stages"
      caption="The first example is a real gpt-4.1-mini sentence. The others are the kinds of mistakes the check exists for, each run through the real check."
      toolbar={
        <ExamplePicker
          label="Try"
          options={SENTENCE_EXAMPLES.map((e) => ({ id: e.id, label: e.label }))}
          value={id}
          onChange={setId}
        />
      }
    >
      <div className="grid gap-4 md:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)]">
        <div className="rounded-lg border border-line bg-paper p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="text-sm font-medium text-ink">Facts for The Hobbit</span>
            <Actor kind="code" />
          </div>
          <dl className="mt-3 grid gap-1.5">
            {HOBBIT_FACTS.map((fact) => (
              <div key={fact.field} className="flex flex-col">
                <dt className="font-mono text-caption text-ink-secondary">{fact.field}</dt>
                <dd className="font-medium text-ink">{fact.value}</dd>
              </div>
            ))}
          </dl>
          <p className="mt-3 text-caption text-ink-secondary">The AI sees only these facts, never the book itself.</p>
        </div>

        <div className="flex flex-col gap-3">
          <div className={reveal(step, 1, "rounded-lg border border-ai-line bg-ai-surface p-4")}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="flex items-center gap-2 text-sm font-medium text-ink">
                <SparklesIcon aria-hidden="true" className="size-4 text-ai" />
                The AI writes one sentence
              </span>
              <Actor kind="ai" />
            </div>
            <p className="mt-2 text-lg text-pretty text-ink">
              {words.map((piece, i) => {
                if (/^\s+$/.test(piece)) return piece
                const n = wordIndex++
                const flagged = step >= 2 && example.flagged.includes(bare(piece))
                return (
                  <span
                    key={i}
                    className={cn(
                      "rounded-sm transition-[opacity,background-color,color] duration-300",
                      step >= 1 ? "opacity-100" : "opacity-0",
                      flagged &&
                        "bg-danger-surface text-danger underline decoration-danger decoration-wavy underline-offset-4"
                    )}
                    style={{ transitionDelay: step === 1 ? `${n * 90}ms` : "0ms" }}
                  >
                    {piece}
                  </span>
                )
              })}
            </p>
          </div>

          <div
            className={reveal(
              step,
              2,
              cn("rounded-lg border p-4", passed ? "border-teal/40 bg-sage/60" : "border-danger-line bg-danger-surface")
            )}
          >
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="flex items-center gap-2 text-sm font-medium text-ink">
                {passed ? (
                  <CircleCheckIcon aria-hidden="true" className="size-4 text-teal" />
                ) : (
                  <CircleXIcon aria-hidden="true" className="size-4 text-danger" />
                )}
                {passed ? "Every word is backed by the facts" : "The check rejects it"}
              </span>
              <Actor kind="code" />
            </div>
            {passed ? (
              <p className="mt-1 text-sm text-ink">
                Names, numbers and match words all appear in the facts, and the sentence is one calm statement.
              </p>
            ) : (
              <ul className="mt-1 list-disc pl-5 text-sm text-ink">
                {example.problems.map((problem) => (
                  <li key={problem}>{problem}</li>
                ))}
              </ul>
            )}
          </div>

          <div className={reveal(step, 3, "rounded-lg border border-line bg-surface p-4 shadow-panel")}>
            <p className="text-caption text-ink-secondary">
              {passed
                ? "What you see under the book"
                : "What you see instead: a sentence code writes itself from what it knows"}
            </p>
            <p className="mt-1 text-pretty text-ink">{passed ? example.sentence : TEMPLATE_SENTENCE}</p>
          </div>
        </div>
      </div>
    </Figure>
  )
}
