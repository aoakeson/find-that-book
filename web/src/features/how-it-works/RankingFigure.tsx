import { useState } from "react"
import { cn } from "@/lib/utils"
import { RANK_EXAMPLES, SCORE_TERMS, type ScoreTerm, type ScoredBook } from "./examples"
import { ExamplePicker, Figure } from "./figure"
import { useSequence } from "./use-sequence"

const STAGES = ["Two books", "Title", "Author", "Popularity", "Year and characters", "Penalties", "Result"]
const DURATIONS = [1400, 1500, 1500, 1800, 1600, 1800, 3200]
/** The step at which each part of the score is added. */
const TERM_STEP: Record<ScoreTerm, number> = {
  title: 1,
  author: 2,
  editions: 3,
  readers: 3,
  year: 4,
  characters: 4,
  penalty: 5,
}
const POSITIVE: ScoreTerm[] = ["title", "author", "editions", "readers", "year", "characters"]
const SEGMENT: Record<ScoreTerm, string> = {
  title: "bg-teal",
  author: "bg-[#5e8f88]",
  editions: "bg-[#86b0a9]",
  readers: "bg-[#b3cfc9]",
  year: "bg-ink/55",
  characters: "bg-[#c4a35a]",
  penalty: "",
}
const SCALE = 17
const NAME = Object.fromEntries(SCORE_TERMS.map((t) => [t.term, t.name])) as Record<ScoreTerm, string>

const pct = (value: number) => `${(Math.max(0, value) / SCALE) * 100}%`
const amount = (value: number) => (Number.isInteger(value) ? String(Math.abs(value)) : Math.abs(value).toFixed(2))
const signed = (value: number) => (value < 0 ? `−${amount(value)}` : `+${amount(value)}`)

function ScoreBar({ book, step }: { book: ScoredBook; step: number }) {
  const positive = POSITIVE.reduce((sum, term) => sum + (step >= TERM_STEP[term] ? (book.terms[term] ?? 0) : 0), 0)
  const penalty = step >= TERM_STEP.penalty ? (book.terms.penalty ?? 0) : 0
  const showBonus = step >= 6 && book.orderBonus

  return (
    <div className="relative h-7 w-full overflow-hidden rounded-md bg-mist" aria-hidden="true">
      <div className="absolute inset-0 flex">
        {POSITIVE.map((term) => {
          const value = step >= TERM_STEP[term] ? (book.terms[term] ?? 0) : 0
          return (
            <span
              key={term}
              className={cn(
                "h-full transition-[width] duration-700 ease-(--ease-out-soft)",
                SEGMENT[term],
                value > 0 && "border-r border-surface/70"
              )}
              style={{ width: pct(value) }}
            />
          )
        })}
        {showBonus ? (
          <span
            className="h-full border-2 border-dashed border-teal bg-sage transition-[width] duration-700"
            style={{ width: pct(book.orderBonus ?? 0) }}
          />
        ) : null}
      </div>
      <span
        className="hiw-stripes absolute inset-y-0 transition-[left,width,opacity] duration-700 ease-(--ease-out-soft)"
        style={{
          left: pct(positive + penalty),
          width: pct(-penalty),
          opacity: penalty < 0 ? 1 : 0,
        }}
      />
    </div>
  )
}

function BookCard({ book, step, rank }: { book: ScoredBook; step: number; rank: number }) {
  const done = step >= 6
  const shownTerms = (Object.entries(book.terms) as [ScoreTerm, number][]).filter(([term]) => step >= TERM_STEP[term])
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-line bg-surface p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-medium text-pretty text-ink">
            <span className="sr-only">Book {rank}: </span>
            {book.title}
          </p>
          <p className="text-sm text-ink-secondary">{book.detail}</p>
        </div>
        <div
          className={cn(
            "flex shrink-0 flex-col items-end transition-opacity duration-500",
            done ? "opacity-100" : "opacity-0"
          )}
        >
          <span className="text-2xl leading-none font-medium text-ink tabular-nums">{book.total.toFixed(2)}</span>
          <span className="mt-1 rounded-full bg-mist px-2 py-0.5 text-caption font-medium text-ink">
            Tier {book.tier}
          </span>
        </div>
      </div>
      <ScoreBar book={book} step={step} />
      <ul className="flex min-h-7 flex-wrap gap-1.5" aria-label="Score parts">
        {shownTerms.map(([term, value]) => (
          <li
            key={term}
            className={cn(
              "flex items-center gap-1.5 rounded-full px-2 py-0.5 text-caption ring-1",
              term === "penalty" ? "bg-danger-surface text-danger ring-danger/30" : "bg-paper text-ink ring-line"
            )}
          >
            {term !== "penalty" ? (
              <span aria-hidden="true" className={cn("size-2.5 rounded-full", SEGMENT[term])} />
            ) : null}
            {NAME[term]} <span className="font-medium tabular-nums">{signed(value)}</span>
          </li>
        ))}
        {done && book.orderBonus ? (
          <li className="flex items-center gap-1.5 rounded-full border border-dashed border-teal bg-sage px-2 py-0.5 text-caption text-ink">
            Tier B bonus, for sorting only <span className="font-medium tabular-nums">+{book.orderBonus}</span>
          </li>
        ) : null}
      </ul>
    </div>
  )
}

export function RankingFigure() {
  const [id, setId] = useState(RANK_EXAMPLES[0].id)
  const example = RANK_EXAMPLES.find((e) => e.id === id) ?? RANK_EXAMPLES[0]
  const sequence = useSequence(STAGES.length, DURATIONS, id)
  const { step } = sequence

  return (
    <Figure
      sequence={sequence}
      stages={STAGES}
      label="Scoring stages"
      caption="Real scores from the ranking code, rounded to two decimals. The list is sorted by score; a tier B book gets 2 extra points for sorting only, so the version by a contributor you named comes first."
      toolbar={
        <ExamplePicker
          label="Try"
          options={RANK_EXAMPLES.map((e) => ({ id: e.id, label: e.typed }))}
          value={id}
          onChange={setId}
        />
      }
    >
      <div className="grid gap-3">
        <p className="text-sm text-ink-secondary">
          Search: <span className="font-medium text-ink">{example.typed}</span>
        </p>
        {example.books.map((book, i) => (
          <BookCard key={book.title} book={book} step={step} rank={i + 1} />
        ))}
        <p
          className={cn(
            "rounded-lg bg-sage px-4 py-3 text-pretty text-ink transition-opacity duration-500",
            step >= 6 ? "opacity-100" : "opacity-0"
          )}
        >
          {example.verdict}
        </p>
      </div>
    </Figure>
  )
}
