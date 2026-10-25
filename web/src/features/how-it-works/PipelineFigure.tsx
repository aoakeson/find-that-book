import { CircleCheckIcon } from "lucide-react"
import { cn } from "@/lib/utils"
import { Actor, Figure, reveal } from "./figure"
import { useSequence } from "./use-sequence"

const TOTAL_MS = 3477

interface Lane {
  name: string
  actor: "ai" | "code"
  start: number
  end: number
  /** The step at which this lane runs. */
  at: number
  output: string
}

/** Stage timings as the API measured them for this query (meta.timings_ms); rank runs at the end of retrieval. */
const LANES: Lane[] = [
  { name: "Clean the words", actor: "code", start: 0, end: 4, at: 1, output: "tolkien hobbit, plus the year 1937" },
  { name: "Read the query", actor: "ai", start: 4, end: 1502, at: 2, output: "The Hobbit, by J.R.R. Tolkien" },
  { name: "Search Open Library", actor: "code", start: 4, end: 1544, at: 2, output: "20 books came back" },
  { name: "Rank and decide", actor: "code", start: 1515, end: 1544, at: 3, output: "The Hobbit: tier A, best match" },
  { name: "Write the reason", actor: "ai", start: 1544, end: TOTAL_MS, at: 4, output: "One sentence, checked by code" },
]

const STAGES = ["You type", "Clean", "Read and search", "Rank", "Explain", "Answer"]
const DURATIONS = [1600, 1100, 2600, 1100, 2400, 3000]

export function PipelineFigure() {
  const sequence = useSequence(STAGES.length, DURATIONS)
  const { step, animated } = sequence

  return (
    <Figure
      sequence={sequence}
      stages={STAGES}
      label="Search stages"
      caption="A recorded run of this exact query: one Open Library call and two AI calls (gpt-4.1-mini), 3.5 seconds in all. The AI reads while Open Library is still answering, so it adds almost no wait."
    >
      <div className="flex flex-col gap-5">
        <QueryLine step={step} />

        <div className="grid gap-3" role="list" aria-label="Measured timeline">
          {LANES.map((lane) => {
            const state = step > lane.at ? "done" : step === lane.at ? "running" : "waiting"
            const left = (lane.start / TOTAL_MS) * 100
            const width = ((lane.end - lane.start) / TOTAL_MS) * 100
            return (
              <div
                role="listitem"
                key={lane.name}
                className="grid grid-cols-1 gap-x-4 gap-y-1.5 sm:grid-cols-[11rem_minmax(0,1fr)] sm:items-center"
              >
                <div
                  className={cn(
                    "flex items-center gap-2 transition-opacity duration-300",
                    state === "waiting" && "opacity-55"
                  )}
                >
                  <span className="text-sm font-medium text-ink">{lane.name}</span>
                  <Actor kind={lane.actor} />
                </div>
                <div className="flex min-w-0 flex-col gap-1">
                  <div className="relative h-6 rounded-md bg-mist">
                    <div
                      aria-hidden="true"
                      className={cn(
                        "absolute inset-y-1 origin-left rounded-sm",
                        lane.actor === "ai" ? "bg-ai" : "bg-teal",
                        animated && "transition-transform ease-linear"
                      )}
                      style={{
                        left: `${left}%`,
                        width: `max(${width}%, 0.375rem)`,
                        transform: `scaleX(${state === "waiting" ? 0 : 1})`,
                        transitionDuration: animated && state === "running" ? `${DURATIONS[lane.at] * 0.8}ms` : "0ms",
                      }}
                    />
                  </div>
                  <p className={reveal(state === "done" ? 1 : 0, 1, "text-sm text-ink")}>
                    {lane.output} <span className="text-ink-secondary">({formatMs(lane.end - lane.start)})</span>
                  </p>
                </div>
              </div>
            )
          })}
          <TimeAxis />
        </div>

        <AnswerCard step={step} />
      </div>
    </Figure>
  )
}

function QueryLine({ step }: { step: number }) {
  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
      <span className="text-sm text-ink-secondary">You type</span>
      <span className="rounded-composer border border-line-strong bg-surface px-4 py-2 text-base font-medium text-ink shadow-composer">
        tolkien hobbit illustrated deluxe 1937
        <span
          aria-hidden="true"
          className={cn("ml-0.5 inline-block h-4 w-px translate-y-0.5 bg-ink", step === 0 ? "hiw-caret" : "opacity-0")}
        />
      </span>
    </div>
  )
}

function TimeAxis() {
  return (
    <div aria-hidden="true" className="grid grid-cols-1 sm:grid-cols-[11rem_minmax(0,1fr)] sm:gap-x-4">
      <span className="hidden sm:block" />
      <div className="relative h-5 text-caption text-ink-secondary">
        {[0, 1000, 2000, 3000].map((ms) => (
          <span
            key={ms}
            className="absolute top-0 -translate-x-1/2 first:translate-x-0"
            style={{ left: `${(ms / TOTAL_MS) * 100}%` }}
          >
            {ms / 1000} s
          </span>
        ))}
        <span className="absolute top-0 right-0">3.5 s</span>
      </div>
    </div>
  )
}

function AnswerCard({ step }: { step: number }) {
  return (
    <div className={reveal(step, 5, "flex gap-4 rounded-panel border border-line bg-paper p-4")}>
      <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-sage text-teal">
        <CircleCheckIcon aria-hidden="true" className="size-5" />
      </span>
      <div className="flex min-w-0 flex-col gap-1">
        <p className="text-sm text-ink-secondary">Best match</p>
        <p className="text-title font-semibold text-ink">
          The Hobbit <span className="font-normal text-ink-secondary">by J.R.R. Tolkien</span>
        </p>
        <p className="text-pretty text-ink">
          Exact title match; J.R.R. Tolkien is the primary author; Open Library's first publication year 1937 matches
          the year in the search; 482 editions.
        </p>
      </div>
    </div>
  )
}

function formatMs(ms: number) {
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(2)} s`
}
