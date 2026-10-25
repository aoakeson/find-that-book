import { getRouteApi } from "@tanstack/react-router"
import { useEffect } from "react"
import { useDocumentTitle } from "@/lib/use-document-title"
import { cn } from "@/lib/utils"
import { Composer } from "./composer/Composer"
import { AiOffNote } from "./AiOffNote"
import { ExampleQueries } from "./composer/ExampleQueries"
import { ResultsRegion } from "./results/ResultsRegion"
import { useSearchSession, viewQuery } from "./session"

const route = getRouteApi("/")

export function SearchPage() {
  const { entry } = route.useSearch()
  const { view, syncEntry } = useSearchSession()
  const home = view.kind === "home"

  // ?entry= drives history restores, back/forward and refresh. Restoring never calls the API.
  useEffect(() => {
    syncEntry(entry)
  }, [entry, syncEntry])

  const query = viewQuery(view)
  useDocumentTitle(query ? `“${truncate(query, 60)}”` : null)

  return (
    <div
      className={cn(
        "mx-auto flex w-full max-w-3xl flex-col px-4 transition-[padding] duration-250 ease-(--ease-out-soft) sm:px-6",
        home ? "pt-[clamp(2.5rem,14vh,9rem)] pb-16" : "pt-4 pb-24 sm:pt-6"
      )}
    >
      {/* Kept (visually hidden) above results so the page always has one h1 and route focus has a target. */}
      <h1
        id="page-heading"
        tabIndex={-1}
        className={cn(
          "text-center font-heading text-heading font-semibold tracking-tight outline-none",
          home ? "mb-6 sm:mb-8" : "sr-only"
        )}
      >
        Which book are you thinking of?
      </h1>
      <Composer compact={!home} />
      {home ? (
        <>
          <ExampleQueries />
          <AiOffNote className="mx-auto mt-8 max-w-prose text-center text-sm text-pretty text-ink-secondary" />
        </>
      ) : (
        <ResultsRegion />
      )}
    </div>
  )
}

function truncate(text: string, max: number): string {
  return text.length > max ? `${text.slice(0, max - 1)}…` : text
}
