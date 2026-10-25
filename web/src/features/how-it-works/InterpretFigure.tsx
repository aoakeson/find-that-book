import { CircleCheckIcon, CircleXIcon, SparklesIcon } from "lucide-react"
import { useState } from "react"
import { cn } from "@/lib/utils"
import { READ_EXAMPLES } from "./examples"
import { Actor, ExamplePicker, Figure, reveal } from "./figure"
import { useSequence } from "./use-sequence"

const STAGES = ["You type", "AI reads", "AI answers", "Code checks", "Search plan"]
const DURATIONS = [1200, 1800, 2200, 3200, 3200]

export function InterpretFigure() {
  const [id, setId] = useState(READ_EXAMPLES[0].id)
  const example = READ_EXAMPLES.find((e) => e.id === id) ?? READ_EXAMPLES[0]
  const sequence = useSequence(STAGES.length, DURATIONS, id)
  const { step } = sequence
  const checking = step >= 3

  return (
    <Figure
      sequence={sequence}
      stages={STAGES}
      label="Interpretation stages"
      caption="The AI may resolve what you meant, but not add clues you never gave. Each verdict is the real checking code’s output; the added characters and the “Mark Twain” title are mistakes gpt-4.1-mini really made in testing."
      toolbar={
        <ExamplePicker
          label="Try"
          options={READ_EXAMPLES.map((e) => ({ id: e.id, label: e.label }))}
          value={id}
          onChange={setId}
        />
      }
    >
      <div className="grid gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <span className="rounded-composer border border-line-strong bg-surface px-4 py-2 text-lg font-medium text-ink shadow-composer">
            {example.typed}
          </span>
        </div>

        <div
          className={cn(
            "flex items-center gap-3 rounded-lg border px-4 py-3 transition-[border-color,background-color] duration-500",
            step >= 1 ? "border-ai-line bg-ai-surface" : "border-line bg-paper"
          )}
        >
          <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-surface text-ai ring-1 ring-ai-line">
            <SparklesIcon aria-hidden="true" className="size-4.5" />
          </span>
          <p className="min-w-0 flex-1 text-sm text-ink">
            The AI reads your words like a librarian would: which book, which author, which characters.
          </p>
          <span aria-hidden="true" className={cn("flex gap-1", step === 1 ? "opacity-100" : "opacity-0")}>
            {[0, 1, 2].map((i) => (
              <span
                key={i}
                className="hiw-dot size-1.5 rounded-full bg-ai"
                style={{ animationDelay: `${i * 160}ms` }}
              />
            ))}
          </span>
        </div>

        <div className={reveal(step, 2, "overflow-hidden rounded-lg border border-ai-line")}>
          <div className="flex items-center justify-between gap-3 border-b border-ai-line bg-ai-surface px-4 py-2">
            <span className="text-sm font-medium text-ink">The AI’s answer</span>
            <span
              className={cn(
                "flex items-center gap-2 text-sm text-ink-secondary transition-opacity duration-300",
                checking ? "opacity-100" : "opacity-0"
              )}
            >
              checked by <Actor kind="code" />
            </span>
          </div>
          <ul className="divide-y divide-line">
            {example.fields.map((field, i) => (
              <li
                key={field.name}
                className={cn(
                  "grid gap-x-4 gap-y-1 px-4 py-3 sm:grid-cols-[6.5rem_minmax(0,1fr)_minmax(0,1.1fr)] sm:items-baseline",
                  "transition-[opacity,translate] duration-500 ease-(--ease-out-soft)",
                  step >= 2 ? "translate-y-0 opacity-100" : "translate-y-1 opacity-0"
                )}
                style={{ transitionDelay: step === 2 ? `${i * 260}ms` : "0ms" }}
              >
                <span className="font-mono text-caption text-ink-secondary">{field.name}</span>
                <span
                  className={cn(
                    "font-medium wrap-break-word transition-colors duration-500",
                    checking && !field.kept
                      ? "text-ink-secondary line-through decoration-danger decoration-2"
                      : "text-ink"
                  )}
                >
                  {field.value}
                </span>
                <span
                  className={cn(
                    "flex items-start gap-1.5 text-sm transition-[opacity,translate] duration-500 ease-(--ease-out-soft)",
                    checking ? "translate-x-0 opacity-100" : "-translate-x-1 opacity-0"
                  )}
                  style={{ transitionDelay: step === 3 ? `${i * 380}ms` : "0ms" }}
                >
                  {field.kept ? (
                    <CircleCheckIcon aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-teal" />
                  ) : (
                    <CircleXIcon aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-danger" />
                  )}
                  <span className="text-ink">
                    <span className={cn("font-medium", field.kept ? "text-teal" : "text-danger")}>
                      {field.kept ? "Kept" : "Removed"}.
                    </span>{" "}
                    {field.why}
                  </span>
                </span>
              </li>
            ))}
          </ul>
        </div>

        <div className={reveal(step, 4, "rounded-lg border border-teal/30 bg-sage/50 p-4")}>
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-medium text-ink">Searches Open Library runs</span>
            <Actor kind="code" />
          </div>
          <ul className="mt-2 flex flex-wrap gap-2">
            {example.requests.map((request) => (
              <li
                key={request}
                className="rounded-md bg-surface px-2.5 py-1 font-mono text-sm wrap-break-word text-ink ring-1 ring-line"
              >
                {request}
              </li>
            ))}
          </ul>
          <p className="mt-3 text-pretty text-ink">{example.outcome}</p>
        </div>
      </div>
    </Figure>
  )
}
