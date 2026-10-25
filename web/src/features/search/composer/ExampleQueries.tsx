import { useId } from "react"
import { ChevronDownIcon } from "lucide-react"
import { Button, buttonVariants } from "@/components/ui/button"
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible"
import { useSearchSession } from "@/features/search/session"
import { cn } from "@/lib/utils"
import { SEARCH_EXAMPLE_GROUPS, STILL_MISSED_TOPICS, type SearchExample } from "./example-catalog"

/** Messy queries straight from the interview brief: they show what kind of text the search understands. */
export const EXAMPLE_QUERIES = [
  "tolkien hobbit illustrated deluxe 1937",
  "mark huckleberry",
  "tale two cities",
] as const

const chipClassName =
  "h-auto min-h-9 max-w-full min-w-0 justify-start border-line bg-surface py-1.5 text-left font-normal whitespace-normal text-ink-secondary hover:bg-mist hover:text-ink pointer-coarse:min-h-11"

/** Filling the composer (not searching) lets the user see, edit and then run the example. */
export function ExampleQueries() {
  const fill = useFillExample()
  return (
    <div className="mt-6 flex w-full flex-col items-center gap-4 sm:mt-8">
      <div role="group" aria-label="Examples" className="flex flex-wrap items-center justify-center gap-2">
        <span aria-hidden className="text-sm text-muted-foreground">
          Try
        </span>
        {EXAMPLE_QUERIES.map((example) => (
          <Button
            key={example}
            type="button"
            variant="outline"
            size="sm"
            onClick={() => fill(example)}
            className={chipClassName}
          >
            {example}
          </Button>
        ))}
      </div>
      <MoreSearchExamples onPick={fill} />
    </div>
  )
}

function useFillExample() {
  const { setDraft, focusComposer } = useSearchSession()
  return (query: string) => {
    setDraft(query)
    focusComposer()
  }
}

/** A collapsed catalog of the kinds of text the search understands. Closed until asked for. */
function MoreSearchExamples({ onPick }: { onPick: (query: string) => void }) {
  const baseId = useId()
  return (
    <Collapsible className="flex w-full min-w-0 flex-col">
      <CollapsibleTrigger
        className={cn(
          buttonVariants({ variant: "link", size: "sm" }),
          "h-auto min-h-9 max-w-full self-center gap-1 px-2 text-center font-normal whitespace-normal underline underline-offset-4 pointer-coarse:min-h-11"
        )}
      >
        More things you can search for
        <ChevronDownIcon aria-hidden className="transition-transform duration-200 ease-(--ease-out-soft) group-aria-expanded/button:rotate-180" />
      </CollapsibleTrigger>
      <CollapsibleContent className="h-(--collapsible-panel-height) w-full min-w-0 overflow-visible transition-[height] duration-200 ease-(--ease-out-soft) data-ending-style:h-0 data-ending-style:overflow-hidden data-starting-style:h-0 data-starting-style:overflow-hidden">
        <div className="flex w-full min-w-0 flex-col gap-4 pt-4 text-left">
          <p className="max-w-measure text-sm text-pretty text-ink-secondary">
            Choosing one fills the search box, and you start the search. Plain examples work with the AI off. Ones
            marked Needs AI need Gemini, OpenAI or Anthropic turned on. The search box also suggests titles as you
            type.
          </p>
          <div className="flex flex-col">
            {SEARCH_EXAMPLE_GROUPS.map((group, index) => {
              const headingId = `${baseId}-${index}`
              return (
                <section key={group.title} className="border-t border-line py-4 first:border-t-0 first:pt-0">
                  <h2 id={headingId} className="text-base font-medium text-ink">
                    {group.title}
                  </h2>
                  {group.note ? <p className="mt-1 text-sm text-pretty text-ink-secondary">{group.note}</p> : null}
                  <div role="group" aria-labelledby={headingId} className="mt-2 flex flex-wrap gap-2">
                    {group.queries.map((example) => (
                      <ExampleChip key={example.query} example={example} onPick={onPick} />
                    ))}
                  </div>
                </section>
              )
            })}
          </div>
          <p className="max-w-measure text-sm text-pretty text-ink-secondary">{missedTopicsSentence()}</p>
        </div>
      </CollapsibleContent>
    </Collapsible>
  )
}

function missedTopicsSentence(): string {
  const [venice, cookbook, lincoln] = STILL_MISSED_TOPICS
  return `These still miss, even with the AI on: “${venice}”, “${cookbook}” and “${lincoln}”.`
}

function ExampleChip({ example, onPick }: { example: SearchExample; onPick: (query: string) => void }) {
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={() => onPick(example.query)}
      aria-label={example.needsAi ? `${example.query}, Needs AI` : undefined}
      className={cn(chipClassName, "flex-wrap gap-1.5")}
    >
      <span className="min-w-0 wrap-break-word">{example.query}</span>
      {example.needsAi ? (
        <span className="shrink-0 rounded-full bg-sage px-1.5 py-0.5 text-caption font-medium whitespace-nowrap text-teal">
          Needs AI
        </span>
      ) : null}
    </Button>
  )
}
