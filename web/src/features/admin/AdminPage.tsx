import { Link } from "@tanstack/react-router"
import { ChevronLeftIcon } from "lucide-react"
import { currentEntryId, useSearchSession } from "@/features/search/session"
import { useDocumentTitle } from "@/lib/use-document-title"
import { AiProviderSection } from "./AiProviderSection"

/** Development-only settings. Sections stack in one column; each owns its data and states. */
export function AdminPage() {
  useDocumentTitle("Admin")
  const { view } = useSearchSession()
  // Back to the answer that was on screen (restored from history, never searched again), or to the plain search page.
  const entry = currentEntryId(view)

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-8 px-4 pt-4 pb-24 sm:px-6 sm:pt-6">
      <div className="flex flex-col items-start gap-2">
        <Link
          to="/"
          search={entry ? { entry } : {}}
          className="-ml-2 inline-flex min-h-10 items-center gap-1 rounded-lg px-2 text-sm font-medium text-ink-secondary transition-colors duration-150 hover:bg-muted hover:text-ink pointer-coarse:min-h-11"
        >
          <ChevronLeftIcon aria-hidden="true" className="size-4" />
          Back to search
        </Link>
        <h1
          id="page-heading"
          tabIndex={-1}
          className="font-heading text-heading font-semibold tracking-tight outline-none"
        >
          Admin
        </h1>
      </div>
      <AiProviderSection />
    </div>
  )
}
