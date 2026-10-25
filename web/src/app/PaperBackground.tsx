import { useEffect, useRef, useSyncExternalStore, type CSSProperties } from "react"
import { useMediaQuery, useMotion } from "@/lib/motion"

const FINE_POINTER = "(hover: hover) and (pointer: fine)"

/** Back to front: mist, sage, white. Edges are open paths in a 1440×320 box stretched to the viewport width. */
const LAYERS = [
  {
    edge: "M0 138C380 74 900 36 1440 58",
    fill: "var(--mist)",
    stroke: "var(--line)",
    depth: 2,
    drift: "-72s",
    duration: "96s",
  },
  {
    edge: "M0 150C460 150 1000 196 1440 172",
    fill: "var(--sage)",
    stroke: "color-mix(in oklab, var(--sage), var(--teal) 14%)",
    depth: 4,
    drift: "-31s",
    duration: "74s",
  },
  {
    edge: "M0 262C420 222 980 214 1440 250",
    fill: "var(--surface)",
    stroke: "var(--line)",
    depth: 6,
    drift: "-12s",
    duration: "58s",
  },
] as const

/**
 * Layered paper edges along the bottom of the viewport. Fixed behind the page, never interactive. Drift is CSS-only;
 * parallax (fine pointers only) writes two CSS variables at most once per frame, so React never re-renders on move.
 */
/** `quiet` lowers it wherever text scrolls over it; full strength only behind the empty home composer. */
export function PaperBackground({ quiet = false }: { quiet?: boolean }) {
  const { animate, reducedMotion } = useMotion()
  const visible = useDocumentVisible()
  const finePointer = useMediaQuery(FINE_POINTER)
  const ref = useRef<HTMLDivElement>(null)
  const running = animate && visible

  useEffect(() => {
    const el = ref.current
    if (!el || !running || !finePointer) return
    let frame = 0
    let x = 0
    let y = 0
    const onMove = (event: PointerEvent) => {
      x = (event.clientX / window.innerWidth) * 2 - 1
      y = (event.clientY / window.innerHeight) * 2 - 1
      if (frame) return
      frame = requestAnimationFrame(() => {
        frame = 0
        el.style.setProperty("--paper-x", x.toFixed(3))
        el.style.setProperty("--paper-y", y.toFixed(3))
      })
    }
    window.addEventListener("pointermove", onMove, { passive: true })
    return () => {
      window.removeEventListener("pointermove", onMove)
      cancelAnimationFrame(frame)
      el.style.removeProperty("--paper-x")
      el.style.removeProperty("--paper-y")
    }
  }, [running, finePointer])

  return (
    <div
      ref={ref}
      aria-hidden="true"
      data-motion={running ? "running" : reducedMotion ? "off" : "paused"}
      data-quiet={quiet || undefined}
      className="paper pointer-events-none fixed inset-x-0 bottom-0 z-0 h-[clamp(10rem,34svh,24rem)] overflow-hidden select-none"
    >
      {LAYERS.map((layer) => (
        <div key={layer.depth} className="paper-layer" style={{ "--paper-depth": layer.depth } as CSSProperties}>
          <div
            className="paper-drift"
            style={{ "--paper-drift-duration": layer.duration, "--paper-drift-delay": layer.drift } as CSSProperties}
          >
            <svg viewBox="0 0 1440 320" preserveAspectRatio="none" className="block size-full" focusable="false">
              <path d={`${layer.edge}V320H0Z`} fill={layer.fill} />
              <path
                d={layer.edge}
                fill="none"
                stroke={layer.stroke}
                strokeWidth="1"
                vectorEffect="non-scaling-stroke"
              />
            </svg>
          </div>
        </div>
      ))}
    </div>
  )
}

function subscribeVisibility(onChange: () => void) {
  document.addEventListener("visibilitychange", onChange)
  return () => document.removeEventListener("visibilitychange", onChange)
}

function useDocumentVisible(): boolean {
  return useSyncExternalStore(
    subscribeVisibility,
    () => document.visibilityState !== "hidden",
    () => true
  )
}
