import { CircleCheckIcon, CircleXIcon } from "lucide-react"
import { cn } from "@/lib/utils"
import { TYPO_PAIRS } from "./examples"
import { Figure } from "./figure"
import { useSequence } from "./use-sequence"

const STAGES = TYPO_PAIRS.map((p) => p.typed)

/** Marks the letters of `a` that are not part of its longest common subsequence with `b`. */
function differing(a: string, b: string): boolean[] {
  const table = Array.from({ length: a.length + 1 }, () => new Array<number>(b.length + 1).fill(0))
  for (let i = a.length - 1; i >= 0; i--)
    for (let j = b.length - 1; j >= 0; j--)
      table[i][j] = a[i] === b[j] ? table[i + 1][j + 1] + 1 : Math.max(table[i + 1][j], table[i][j + 1])
  const marks = new Array<boolean>(a.length).fill(true)
  let i = 0
  let j = 0
  while (i < a.length && j < b.length) {
    if (a[i] === b[j]) {
      marks[i] = false
      i++
      j++
    } else if (table[i + 1][j] >= table[i][j + 1]) i++
    else j++
  }
  return marks
}

function Word({ text, other, show }: { text: string; other: string; show: boolean }) {
  const marks = differing(text, other)
  return (
    <span className="font-mono text-lg tracking-wide text-ink">
      {[...text].map((letter, i) => (
        <span
          key={i}
          className={cn(
            "rounded-sm transition-colors duration-500",
            show && marks[i] && "bg-ai-surface text-ai underline decoration-ai decoration-2 underline-offset-4"
          )}
        >
          {letter}
        </span>
      ))}
    </span>
  )
}

export function TypoFigure() {
  const sequence = useSequence(STAGES.length, 1500)
  const { step } = sequence

  return (
    <Figure
      sequence={sequence}
      stages={STAGES}
      label="Typo examples"
      caption="A word may be one slip away: a letter added, missing or changed, or two letters swapped. Words of 8 letters or more may have two slips. The differing letters are underlined."
    >
      <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {TYPO_PAIRS.map((pair, i) => {
          const shown = step >= i
          return (
            <li
              key={pair.typed}
              className={cn(
                "flex flex-col gap-2 rounded-lg border p-3 transition-[border-color,background-color] duration-500",
                shown
                  ? pair.same
                    ? "border-teal/40 bg-sage/60"
                    : "border-line-strong bg-surface"
                  : "border-line bg-surface"
              )}
            >
              <div className="flex items-baseline justify-between gap-3">
                <span className="flex flex-col">
                  <span className="text-caption text-ink-secondary">You type</span>
                  <Word text={pair.typed} other={pair.title} show={shown} />
                </span>
                <span className="flex flex-col text-right">
                  <span className="text-caption text-ink-secondary">Title has</span>
                  <Word text={pair.title} other={pair.typed} show={shown} />
                </span>
              </div>
              <p
                className={cn(
                  "flex items-start gap-1.5 text-sm transition-opacity duration-500",
                  shown ? "opacity-100" : "opacity-0"
                )}
              >
                {pair.same ? (
                  <CircleCheckIcon aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-teal" />
                ) : (
                  <CircleXIcon aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-danger" />
                )}
                <span className="text-ink">
                  <span className={cn("font-medium", pair.same ? "text-teal" : "text-danger")}>
                    {pair.same ? "Same word." : "Different word."}
                  </span>{" "}
                  {pair.why}
                </span>
              </p>
            </li>
          )
        })}
        <li
          className={cn(
            "flex flex-col justify-center gap-1 rounded-lg border border-dashed border-line-strong p-3 text-sm text-ink transition-opacity duration-500 sm:col-span-2 lg:col-span-1",
            step >= STAGES.length - 1 ? "opacity-100" : "opacity-0"
          )}
        >
          <span className="font-medium">Whole titles work the same way.</span>
          <span>
            <span className="font-mono">murdr on the orient express</span> still counts as the exact title{" "}
            <i>Murder on the Orient Express</i>. But <span className="font-mono">little prince</span> is only a half
            match for <i>A Little Princess</i>.
          </span>
        </li>
      </ul>
    </Figure>
  )
}
