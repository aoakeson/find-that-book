import { useLocation, useNavigate } from "@tanstack/react-router"
import { HistoryIcon, InfoIcon } from "lucide-react"
import { useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { HeaderActionButton } from "@/app/HeaderAction"
import { Button } from "@/components/ui/button"
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import { currentEntryId, useSearchSession } from "@/features/search/session"
import { formatCount } from "@/lib/format"
import { cn } from "@/lib/utils"
import { HistoryList } from "./HistoryList"
import { historyStore, type HistoryEntry, type HistoryProblem } from "./storage"
import { useHistory } from "./use-history"

const PROBLEM_MESSAGES: Record<Exclude<HistoryProblem, null>, string> = {
  unavailable: "This browser isn't keeping history, so searches won't be saved here. Searching still works.",
  full: "Browser storage is full, so new searches aren't being saved.",
  reset: "Saved history couldn't be read and was cleared.",
}

/** The header's History action: a sheet listing this browser's saved searches. */
export function HistoryButton() {
  const [open, setOpen] = useState(false)
  const { focusComposer } = useSearchSession()
  const navigate = useNavigate()
  const pathname = useLocation({ select: (l) => l.pathname })
  // "Start a search" hands focus to the composer instead of back to this button.
  const focusComposerOnClose = useRef(false)

  const startSearch = () => {
    focusComposerOnClose.current = true
    if (pathname !== "/") void navigate({ to: "/" })
    setOpen(false)
  }

  return (
    <Sheet
      open={open}
      onOpenChange={setOpen}
      onOpenChangeComplete={(isOpen) => {
        if (isOpen || !focusComposerOnClose.current) return
        focusComposerOnClose.current = false
        focusComposer()
      }}
    >
      {/* The shell's header action: icon-only with a tooltip on phones, icon + label from sm up, 44px on touch. */}
      <SheetTrigger render={<HeaderActionButton icon={<HistoryIcon aria-hidden="true" />} label="History" />} />
      <SheetContent
        side="right"
        className={cn(
          "gap-0 data-[side=right]:w-full data-[side=right]:sm:max-w-md",
          // Base UI leaves a nested dialog's backdrop out, so the sheet dims itself while "Clear history?" is open.
          "after:pointer-events-none after:absolute after:inset-0 after:bg-black/10 after:opacity-0 after:transition-opacity after:duration-150 after:content-[''] data-nested-dialog-open:after:opacity-100"
        )}
        finalFocus={() => !focusComposerOnClose.current}
      >
        <HistoryPanel pathname={pathname} onClose={() => setOpen(false)} onStartSearch={startSearch} />
      </SheetContent>
    </Sheet>
  )
}

function HistoryPanel({
  pathname,
  onClose,
  onStartSearch,
}: {
  pathname: string
  onClose: () => void
  onStartSearch: () => void
}) {
  const { entries, problem } = useHistory()
  const { view, restoreEntry } = useSearchSession()
  const navigate = useNavigate()
  const currentId = pathname === "/" ? currentEntryId(view) : null
  const [confirmOpen, setConfirmOpen] = useState(false)
  // Frozen when the dialog opens, so its text doesn't change to "0 searches" while it fades out.
  const [confirmCount, setConfirmCount] = useState(0)
  const listRef = useRef<HTMLUListElement>(null)
  const emptyActionRef = useRef<HTMLButtonElement>(null)
  // After a delete the row (and its menu trigger) is gone: focus the next row, the previous one, or the empty state.
  const pendingFocus = useRef<string | null>(null)
  const clearedRef = useRef(false)

  useEffect(() => {
    const target = pendingFocus.current
    if (target === null) return
    pendingFocus.current = null
    const row = target ? listRef.current?.querySelector<HTMLElement>(`[data-entry-id="${target}"]`) : null
    ;(row ?? emptyActionRef.current)?.focus()
  }, [entries])

  const select = (entry: HistoryEntry) => {
    onClose()
    // Restore directly too: when this entry is already in the URL (with a newer search running), navigating is a no-op.
    restoreEntry(entry.id)
    void navigate({ to: "/", search: { entry: entry.id } })
  }

  const remove = (entry: HistoryEntry) => {
    const index = entries.findIndex((e) => e.id === entry.id)
    pendingFocus.current = (entries[index + 1] ?? entries[index - 1])?.id ?? ""
    historyStore.remove(entry.id)
    toast.success("Search deleted")
  }

  const clear = () => {
    clearedRef.current = true
    historyStore.clear()
    setConfirmOpen(false)
    toast.success("History cleared")
  }

  const count = entries.length

  return (
    <>
      <SheetHeader className="border-b border-line px-4 pt-4 pr-14 pb-3 sm:px-6 sm:pr-14">
        <SheetTitle className="text-title font-semibold">Search history</SheetTitle>
        <SheetDescription>Saved on this device only.</SheetDescription>
      </SheetHeader>

      {/* px-1 leaves room for focus outlines inside the scroll box; rows add the rest to line up with the header. */}
      <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto overscroll-contain px-1 py-3 sm:px-3">
        {problem && (
          <p className="mx-3 flex gap-2 rounded-lg bg-mist px-3 py-2 text-sm text-ink-secondary">
            <InfoIcon aria-hidden="true" className="mt-1 size-4 shrink-0" />
            <span>{PROBLEM_MESSAGES[problem]}</span>
          </p>
        )}
        {count > 0 ? (
          <HistoryList ref={listRef} entries={entries} currentId={currentId} onSelect={select} onDelete={remove} />
        ) : (
          <Empty className="flex-none py-10">
            <EmptyHeader>
              <EmptyMedia
                variant="icon"
                className="size-10 rounded-lg bg-sage text-teal [&_svg:not([class*='size-'])]:size-5"
              >
                <HistoryIcon aria-hidden="true" />
              </EmptyMedia>
              <EmptyTitle className="text-base font-semibold">No saved searches yet</EmptyTitle>
              <EmptyDescription>Searches you run are saved here, on this device only.</EmptyDescription>
            </EmptyHeader>
            <EmptyContent>
              <Button ref={emptyActionRef} onClick={onStartSearch}>
                Start a search
              </Button>
            </EmptyContent>
          </Empty>
        )}
      </div>

      {count > 0 && (
        <SheetFooter className="border-t border-line px-4 py-3 sm:px-6">
          <Button
            variant="outline"
            className="self-start"
            onClick={() => {
              clearedRef.current = false
              setConfirmCount(count)
              setConfirmOpen(true)
            }}
          >
            Clear history
          </Button>
        </SheetFooter>
      )}

      {/* Outside the footer, which disappears once the list is empty, so the dialog can finish closing. */}
      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent
          className="data-[size=default]:max-w-[calc(100%-2rem)] data-[size=default]:sm:max-w-sm"
          finalFocus={() => (clearedRef.current ? (emptyActionRef.current ?? true) : true)}
        >
          <AlertDialogHeader>
            <AlertDialogTitle>Clear all saved searches?</AlertDialogTitle>
            <AlertDialogDescription>
              This removes {confirmCount === 1 ? "1 search" : `${formatCount(confirmCount)} searches`} from this
              browser. It can't be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={clear}>
              Clear history
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
