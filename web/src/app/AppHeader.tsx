import { Link, useNavigate } from "@tanstack/react-router"
import { SettingsIcon, SquarePenIcon, WorkflowIcon } from "lucide-react"
import { HistoryButton } from "@/features/history/HistoryButton"
import { useSearchSession } from "@/features/search/session"
import { cn } from "@/lib/utils"
import { BookMark } from "./BookMark"
import { HeaderActionButton, HeaderActionLink } from "./HeaderAction"

export function AppHeader() {
  const { reset, focusComposer } = useSearchSession()
  const navigate = useNavigate()

  const startNewSearch = () => {
    reset()
    // Focus after the search page has rendered, or a route change would take focus back to its heading.
    void navigate({ to: "/", search: {} }).then(focusComposer)
  }

  return (
    <header className="relative z-10">
      <div className="mx-auto flex h-16 w-full max-w-5xl items-center justify-between gap-2 px-4 sm:px-6">
        <BrandLink markOnlyWhenNarrow />
        <div className="flex shrink-0 items-center gap-0.5 sm:gap-1">
          <HeaderActionLink icon={<WorkflowIcon />} label="How it works" to="/how-it-works" />
          <HistoryButton />
          <HeaderActionButton icon={<SquarePenIcon />} label="New search" onClick={startNewSearch} />
          <HeaderActionLink icon={<SettingsIcon />} label="Admin" to="/admin" />
        </div>
      </div>
    </header>
  )
}

/**
 * The mark and product name, linking home. Also used by the minimal shell on error screens. Beside the four header
 * actions the full name needs about 23rem, so below that `markOnlyWhenNarrow` keeps the name for screen readers only.
 */
export function BrandLink({ markOnlyWhenNarrow = false }: { markOnlyWhenNarrow?: boolean }) {
  return (
    <Link
      to="/"
      className="-mx-1 flex min-h-11 min-w-0 items-center gap-2 rounded-lg px-1 font-heading text-base font-semibold tracking-tight text-foreground"
    >
      <BookMark className="size-7 text-teal" />
      <span className={cn("truncate", markOnlyWhenNarrow && "max-[23rem]:sr-only")}>Find That Book</span>
    </Link>
  )
}
