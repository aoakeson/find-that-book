import { PauseIcon, PlayIcon } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import { useMotion } from "@/lib/motion"
import { cn } from "@/lib/utils"

/**
 * Pauses the decorative motion (paper drift, parallax, page turn, shimmer). A toggle with a constant name and
 * aria-pressed; only the icon and tooltip change. Hidden when the OS already asks for reduced motion.
 */
export function MotionToggle({ className }: { className?: string }) {
  const { reducedMotion, paused, setPaused } = useMotion()
  if (reducedMotion) return null
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button
            variant="outline"
            size="icon-sm"
            aria-pressed={paused}
            onClick={() => setPaused(!paused)}
            className={cn(
              "rounded-full bg-surface/90 text-ink-secondary hover:bg-surface hover:text-foreground pointer-coarse:size-11",
              className
            )}
          />
        }
      >
        {paused ? <PlayIcon /> : <PauseIcon />}
        <span className="sr-only">Pause background motion</span>
      </TooltipTrigger>
      <TooltipContent side="left">{paused ? "Resume background motion" : "Pause background motion"}</TooltipContent>
    </Tooltip>
  )
}
