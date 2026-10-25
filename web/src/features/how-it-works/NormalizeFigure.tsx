import { ArrowRightIcon } from "lucide-react"
import { useState, type ReactNode } from "react"
import { cn } from "@/lib/utils"
import { CLEAN_EXAMPLES, TOKEN_LABEL, type CleanExample, type CleanToken } from "./examples"
import { ExamplePicker, Figure, reveal } from "./figure"
import { useSequence } from "./use-sequence"

const STAGES = ["You type", "Fold", "Split into words", "Sort", "Send"]
const DURATIONS = [1300, 2000, 1600, 2600, 3000]

export function NormalizeFigure() {
  const [id, setId] = useState(CLEAN_EXAMPLES[0].id)
  const example = CLEAN_EXAMPLES.find((e) => e.id === id) ?? CLEAN_EXAMPLES[0]
  const sequence = useSequence(STAGES.length, DURATIONS, id)
  const { step } = sequence

  return (
    <Figure
      sequence={sequence}
      stages={STAGES}
      label="Cleaning stages"
      caption={example.takeaway}
      toolbar={
        <ExamplePicker
          label="Try"
          options={CLEAN_EXAMPLES.map((e) => ({ id: e.id, label: e.label }))}
          value={id}
          onChange={setId}
        />
      }
    >
      <div className="grid gap-5">
        <Row label="You type" at={0} step={step}>
          <span className="rounded-composer border border-line-strong bg-surface px-4 py-2 text-lg font-medium wrap-break-word text-ink shadow-composer">
            {example.typed}
          </span>
        </Row>

        <Row label="Fold" at={1} step={step}>
          <div className="flex flex-col gap-1.5">
            <p className="text-lg leading-loose wrap-break-word text-ink">
              <Folded example={example} step={step} />
            </p>
            <p className="max-w-measure text-sm text-ink-secondary">{example.foldNote}</p>
          </div>
        </Row>

        <Row label="Words" at={2} step={step}>
          <ul className="flex flex-wrap items-start gap-2" aria-label="Words">
            {example.tokens.map((token, i) => (
              <TokenChip key={`${token.text}-${i}`} token={token} index={i} step={step} />
            ))}
            {example.joined && (
              <li className={cn("flex items-center gap-2", reveal(step, 3))}>
                <ArrowRightIcon aria-hidden="true" className="size-4 text-ink-secondary" />
                <span className="flex flex-col items-start gap-1">
                  <span className="rounded-lg border border-teal/40 bg-sage px-3 py-1.5 font-medium text-ink">
                    {example.joined}
                  </span>
                  <span className="text-caption text-ink-secondary">searched</span>
                </span>
              </li>
            )}
          </ul>
        </Row>

        <Row label="Send" at={4} step={step}>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="rounded-lg border border-teal/30 bg-sage/60 p-3">
              <p className="text-sm text-ink-secondary">Sent to Open Library</p>
              <p className="mt-1 font-mono text-base font-medium wrap-break-word text-ink">{example.sent}</p>
            </div>
            <div className="rounded-lg border border-line bg-paper p-3">
              <p className="text-sm text-ink-secondary">Kept for later</p>
              {example.keptAside.length > 0 ? (
                <ul className="mt-1 flex flex-col gap-1 text-sm text-ink">
                  {example.keptAside.map((line) => (
                    <li key={line}>{line}</li>
                  ))}
                </ul>
              ) : (
                <p className="mt-1 text-sm text-ink">Nothing: every word is searched.</p>
              )}
            </div>
          </div>
        </Row>
      </div>
    </Figure>
  )
}

function Row({ label, at, step, children }: { label: string; at: number; step: number; children: ReactNode }) {
  return (
    <div className={cn("grid gap-2 sm:grid-cols-[8.5rem_minmax(0,1fr)] sm:items-baseline sm:gap-4", reveal(step, at))}>
      <span className="text-sm text-ink-secondary">{label}</span>
      <div className="min-w-0">{children}</div>
    </div>
  )
}

/** The folded text, with each changed piece showing what was typed above it. */
function Folded({ example, step }: { example: CleanExample; step: number }) {
  return (
    <>
      {example.folded.map((piece, i) => {
        if (piece.was === undefined) return <span key={i}>{piece.text}</span>
        if (piece.text === "")
          return (
            <span key={i} className="mr-0.5 text-ink-secondary line-through decoration-danger decoration-2">
              {piece.was}
              <span className="sr-only"> (dropped)</span>
            </span>
          )
        return (
          <ruby key={i} className="[ruby-position:over]">
            <mark
              className={cn(
                "rounded-sm px-px text-ink transition-colors duration-700",
                step >= 1 ? "bg-sage ring-1 ring-teal/40" : "bg-transparent"
              )}
            >
              {piece.text}
            </mark>
            <rt className="text-caption font-medium text-teal">
              <span className="sr-only"> (typed as </span>
              {piece.was === " " ? "space" : piece.was}
              <span className="sr-only">)</span>
            </rt>
          </ruby>
        )
      })}
    </>
  )
}

const CHIP: Record<CleanToken["kind"], string> = {
  search: "border-teal/40 bg-sage text-ink",
  edition: "border-line bg-paper text-ink-secondary line-through decoration-ink-secondary/70",
  small: "border-dashed border-line-strong bg-surface text-ink-secondary",
  year: "border-teal/50 bg-surface text-teal",
  number: "border-line bg-mist text-ink",
}

function TokenChip({ token, index, step }: { token: CleanToken; index: number; step: number }) {
  const sorted = step >= 3
  return (
    <li
      className={cn(
        "flex flex-col items-start gap-1 transition-[opacity,translate] duration-500 ease-(--ease-out-soft)",
        step >= 2 ? "translate-y-0 opacity-100" : "translate-y-2 opacity-0"
      )}
      style={{ transitionDelay: step === 2 ? `${index * 140}ms` : "0ms" }}
    >
      <span
        className={cn(
          "rounded-lg border px-3 py-1.5 font-medium transition-[color,background-color,border-color,opacity,scale] duration-500",
          sorted ? CHIP[token.kind] : "border-line-strong bg-surface text-ink",
          sorted && token.kind === "number" && "scale-95 opacity-70"
        )}
      >
        {token.text}
      </span>
      <span
        className={cn(
          "text-caption text-ink-secondary transition-opacity duration-500",
          sorted ? "opacity-100" : "opacity-0"
        )}
      >
        {TOKEN_LABEL[token.kind]}
      </span>
    </li>
  )
}
