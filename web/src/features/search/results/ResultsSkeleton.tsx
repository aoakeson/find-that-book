import { Skeleton } from "@/components/ui/skeleton"
import { cn } from "@/lib/utils"

/**
 * Where the answer will appear, sized like a best match plus two alternatives so the page doesn't jump when it
 * lands. Static on purpose: the page-turn book is the only thing that moves while a search runs.
 */
export function ResultsSkeleton() {
  return (
    <div aria-hidden="true" className="flex flex-col gap-6">
      <div className="flex flex-col gap-3">
        <Bar className="h-5 w-24" />
        <div className="grid grid-cols-[5rem_minmax(0,1fr)] gap-x-4 gap-y-3 rounded-panel border border-line bg-surface p-4 shadow-panel sm:grid-cols-[7rem_minmax(0,1fr)] sm:gap-x-6 sm:p-6">
          <Cover className="w-20 sm:row-span-2 sm:w-28" />
          <div className="flex flex-col gap-2 pt-1">
            <Bar className="h-5 w-3/4" />
            <Bar className="h-4 w-1/2" />
            <Bar className="h-3.5 w-2/5" />
          </div>
          <div className="col-span-2 flex flex-col gap-2 sm:col-span-1 sm:col-start-2">
            <Bar className="h-4 w-full" />
            <Bar className="h-4 w-4/5" />
            <Bar className="mt-1 h-3.5 w-24" />
          </div>
        </div>
      </div>
      <div className="flex flex-col gap-1">
        <Bar className="h-5 w-36" />
        <div className="divide-y divide-line">
          {[0, 1].map((i) => (
            <div
              key={i}
              className="grid grid-cols-[3rem_minmax(0,1fr)] gap-x-4 py-5 sm:grid-cols-[3.5rem_minmax(0,1fr)]"
            >
              <Cover className="w-12 sm:w-14" />
              <div className="flex flex-col gap-2 pt-1">
                <Bar className="h-4.5 w-2/3" />
                <Bar className="h-4 w-1/3" />
                <Bar className="h-4 w-full" />
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

function Bar({ className }: { className: string }) {
  return <Skeleton className={cn("animate-none bg-mist", className)} />
}

function Cover({ className }: { className: string }) {
  return <Skeleton className={cn("aspect-2/3 shrink-0 animate-none bg-mist", className)} />
}
