import { EyeIcon, EllipsisIcon, Trash2Icon } from "lucide-react"
import { useId, type Ref } from "react"
import { Button } from "@/components/ui/button"
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu"
import { Item, ItemActions } from "@/components/ui/item"
import { formatDateTime, formatRelativeTime } from "@/lib/format"
import { cn } from "@/lib/utils"
import { outcomeSummary } from "./outcome"
import type { HistoryEntry } from "./storage"

interface HistoryListProps {
  entries: readonly HistoryEntry[]
  /** The entry the search page is showing right now, if any. */
  currentId: string | null
  onSelect: (entry: HistoryEntry) => void
  onDelete: (entry: HistoryEntry) => void
  ref?: Ref<HTMLUListElement>
}

export function HistoryList({ entries, currentId, onSelect, onDelete, ref }: HistoryListProps) {
  return (
    <ul ref={ref} className="flex flex-col gap-1">
      {entries.map((entry) => (
        <HistoryRow
          key={entry.id}
          entry={entry}
          current={entry.id === currentId}
          onSelect={() => onSelect(entry)}
          onDelete={() => onDelete(entry)}
        />
      ))}
    </ul>
  )
}

function HistoryRow({
  entry,
  current,
  onSelect,
  onDelete,
}: {
  entry: HistoryEntry
  current: boolean
  onSelect: () => void
  onDelete: () => void
}) {
  const id = useId()
  return (
    <Item
      render={<li />}
      className={cn(
        "relative flex-nowrap items-start gap-0 p-0 transition-colors duration-150 hover:bg-muted",
        current && "bg-sage hover:bg-sage"
      )}
    >
      {/*
        The button is named by the query alone (WCAG 2.5.3: its visible text is its name); its ::after stretches over
        the row so the whole row stays one target. Outcome and time sit outside it as its description.
      */}
      <div className="flex min-w-0 flex-1 flex-col items-start gap-1 py-3 pr-1 pl-3">
        <button
          type="button"
          data-entry-id={entry.id}
          aria-current={current ? "true" : undefined}
          aria-describedby={`${id}-outcome ${id}-time`}
          onClick={onSelect}
          className="line-clamp-2 cursor-pointer rounded-lg text-left text-base leading-snug font-medium wrap-break-word text-ink outline-offset-4 after:absolute after:inset-0 after:content-['']"
        >
          {entry.query}
        </button>
        <span id={`${id}-outcome`} className="line-clamp-1 text-sm wrap-break-word text-ink-secondary">
          {outcomeSummary(entry.result)}
        </span>
        <span className="flex flex-wrap items-center gap-x-3 gap-y-1 text-caption text-ink-secondary">
          <time id={`${id}-time`} dateTime={entry.savedAt} title={formatDateTime(entry.savedAt)}>
            {formatRelativeTime(entry.savedAt)}
          </time>
          {current && (
            // aria-current already says this to assistive tech.
            <span aria-hidden="true" className="inline-flex items-center gap-1 font-medium text-teal">
              <EyeIcon className="size-3.5" />
              Showing now
            </span>
          )}
        </span>
      </div>
      <ItemActions className="relative z-10 py-2 pr-2">
        <DropdownMenu>
          <DropdownMenuTrigger
            render={<Button variant="ghost" size="icon" className="text-ink-secondary hover:text-ink" />}
            aria-label={`More actions for ${entry.query}`}
          >
            <EllipsisIcon aria-hidden="true" />
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-auto min-w-36">
            <DropdownMenuItem variant="destructive" onClick={onDelete} className="py-2 pointer-coarse:min-h-11">
              <Trash2Icon aria-hidden="true" />
              Delete
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </ItemActions>
    </Item>
  )
}
