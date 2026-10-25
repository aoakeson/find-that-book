import { Link } from "@tanstack/react-router"
import type { ComponentProps, ReactNode } from "react"
import { Button, buttonVariants } from "@/components/ui/button"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import { useMediaQuery } from "@/lib/motion"
import { cn } from "@/lib/utils"

/** Tailwind's `sm`: from here the label is visible, so the tooltip would only repeat it. */
const LABEL_VISIBLE = "(min-width: 40rem)"

const actionClass =
  "px-3 text-ink-secondary hover:text-foreground aria-[current=page]:bg-muted aria-[current=page]:text-foreground max-sm:w-10 max-sm:px-0 max-sm:pointer-coarse:w-11 [&_svg:not([class*='size-'])]:size-[1.125rem]"

interface ActionContentProps {
  icon: ReactNode
  /** Always the accessible name; visible from `sm` up. */
  label: string
}

/**
 * A header action button: icon-only on phones (tooltip as a supplementary label for pointer users), icon + label
 * from `sm` up; 44px tall on touch. Extra props and the ref go to the button, so it can be a trigger itself, e.g.
 * `<SheetTrigger render={<HeaderActionButton icon={<HistoryIcon />} label="History" />} />`.
 */
export function HeaderActionButton({
  icon,
  label,
  className,
  ...props
}: ActionContentProps & Omit<ComponentProps<typeof Button>, "children">) {
  const labelVisible = useMediaQuery(LABEL_VISIBLE)
  return (
    <Tooltip disabled={labelVisible}>
      <TooltipTrigger render={<Button variant="ghost" className={cn(actionClass, className)} {...props} />}>
        {icon}
        <span className="sr-only sm:not-sr-only">{label}</span>
      </TooltipTrigger>
      <TooltipContent side="bottom">{label}</TooltipContent>
    </Tooltip>
  )
}

/** The same look for a route link; it gets `aria-current="page"` (and a muted fill) on its own page. */
export function HeaderActionLink({
  icon,
  label,
  to,
  className,
}: ActionContentProps & { to: "/" | "/admin"; className?: string }) {
  const labelVisible = useMediaQuery(LABEL_VISIBLE)
  return (
    <Tooltip disabled={labelVisible}>
      <TooltipTrigger
        render={<Link to={to} className={cn(buttonVariants({ variant: "ghost" }), actionClass, className)} />}
      >
        {icon}
        <span className="sr-only sm:not-sr-only">{label}</span>
      </TooltipTrigger>
      <TooltipContent side="bottom">{label}</TooltipContent>
    </Tooltip>
  )
}
