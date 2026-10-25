import { useMotion } from "@/lib/motion"
import { cn } from "@/lib/utils"

const RIGHT_PAGE = "M12 6.5c1.8-1.3 4.6-1.9 8.5-1.5v12.5c-3.9-.4-6.7.2-8.5 1.5Z"

/**
 * A small open book. While `active` (a search is running) and motion is allowed, a page turns from right to left on
 * a loop; otherwise it is a still book. CSS-only (keyframes in index.css). Decorative: callers put the status in words.
 */
export function PageTurnBook({ active, className }: { active: boolean; className?: string }) {
  const { animate } = useMotion()
  return (
    <svg
      viewBox="0 0 24 24"
      aria-hidden="true"
      focusable="false"
      data-turning={active && animate}
      className={cn("page-turn size-5 shrink-0 overflow-visible", className)}
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M12 6.5C10.2 5.2 7.4 4.6 3.5 5v12.5c3.9-.4 6.7.2 8.5 1.5Z" fill="var(--surface)" />
      <path d={RIGHT_PAGE} fill="var(--surface)" />
      <path className="page-turn-leaf" d={RIGHT_PAGE} fill="var(--sage)" />
    </svg>
  )
}
