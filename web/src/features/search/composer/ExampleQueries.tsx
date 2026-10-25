import { Button } from "@/components/ui/button"
import { useSearchSession } from "@/features/search/session"

/** Messy queries straight from the interview brief: they show what kind of text the search understands. */
export const EXAMPLE_QUERIES = [
  "tolkien hobbit illustrated deluxe 1937",
  "mark huckleberry",
  "tale two cities",
] as const

/** Filling the composer (not searching) lets the user see, edit and then run the example. */
export function ExampleQueries() {
  const { setDraft, focusComposer } = useSearchSession()
  return (
    <div role="group" aria-label="Examples" className="mt-6 flex flex-wrap items-center justify-center gap-2 sm:mt-8">
      <span aria-hidden className="text-sm text-muted-foreground">
        Try
      </span>
      {EXAMPLE_QUERIES.map((example) => (
        <Button
          key={example}
          type="button"
          variant="outline"
          size="sm"
          onClick={() => {
            setDraft(example)
            focusComposer()
          }}
          className="h-auto min-h-9 max-w-full border-line bg-surface py-1.5 text-left font-normal whitespace-normal text-ink-secondary hover:bg-mist hover:text-ink pointer-coarse:min-h-11"
        >
          {example}
        </Button>
      ))}
    </div>
  )
}
