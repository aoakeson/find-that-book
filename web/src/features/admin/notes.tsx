import type { ReactNode } from "react"
import {
  CircleAlertIcon,
  CircleCheckIcon,
  CircleDashedIcon,
  InfoIcon,
  TriangleAlertIcon,
  type LucideIcon,
} from "lucide-react"
import { cn } from "@/lib/utils"

export type Tone = "ok" | "off" | "warning" | "error" | "info"

const TONES: Record<Tone, { icon: LucideIcon; iconClass: string }> = {
  ok: { icon: CircleCheckIcon, iconClass: "text-teal" },
  off: { icon: CircleDashedIcon, iconClass: "text-ink-secondary" },
  warning: { icon: TriangleAlertIcon, iconClass: "text-warning" },
  error: { icon: CircleAlertIcon, iconClass: "text-danger" },
  info: { icon: InfoIcon, iconClass: "text-ink-secondary" },
}

/** One line of state: always an icon and words, never colour alone. */
export function StateLine({ tone, children, className }: { tone: Tone; children: ReactNode; className?: string }) {
  const { icon: Icon, iconClass } = TONES[tone]
  return (
    <div className={cn("flex min-w-0 items-start gap-2", className)}>
      <Icon aria-hidden="true" className={cn("mt-[0.2em] size-4 shrink-0", iconClass)} />
      <div className="min-w-0 wrap-break-word">{children}</div>
    </div>
  )
}

/** A boxed note for the state of a whole block (the active provider, where changes are kept). */
export function StateNote({ tone, children }: { tone: Tone; children: ReactNode }) {
  return (
    <StateLine
      tone={tone}
      className={cn(
        "rounded-lg border px-3 py-2.5",
        tone === "ok" && "border-transparent bg-sage",
        (tone === "off" || tone === "info") && "border-transparent bg-mist",
        tone === "warning" && "border-warning-line bg-warning-surface",
        tone === "error" && "border-danger-line bg-danger-surface"
      )}
    >
      {children}
    </StateLine>
  )
}

/** An error next to the control it belongs to. Announced when it appears. */
export function InlineError({ id, children }: { id?: string; children: ReactNode }) {
  return (
    <div id={id} role="alert" className="text-sm text-danger">
      <StateLine tone="error">{children}</StateLine>
    </div>
  )
}
