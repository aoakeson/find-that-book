import { useCallback, useEffect, useRef, useState } from "react"
import { useMotion } from "@/lib/motion"

type Status = "waiting" | "playing" | "paused" | "done"

export interface Sequence {
  /** Attach to the illustration: playback starts the first time it is mostly on screen. */
  attach: (node: HTMLElement | null) => void
  /** The step to draw. With reduced motion it is the last one (complete and still) unless the user picked another. */
  step: number
  steps: number
  status: Status
  /** False under reduced motion: nothing plays, so there is nothing to pause or replay. */
  animated: boolean
  /** Pause, resume, or replay from the start once finished. */
  toggle: () => void
  /** Jump to a step and hold it there (paused). */
  goTo: (step: number) => void
  restart: () => void
}

/**
 * Plays an illustration's steps once, from when it scrolls into view. It never loops, so motion stops by itself, and
 * `toggle` pauses it at any point (WCAG 2.2.2). `durations` is per step in ms (the last entry repeats).
 * A change of `resetKey` (a different example picked) plays it again from the start.
 */
export function useSequence(steps: number, durations: number | number[] = 1400, resetKey?: unknown): Sequence {
  const { animate } = useMotion()
  const [step, setStep] = useState(0)
  const [status, setStatus] = useState<Status>("waiting")
  const observer = useRef<IntersectionObserver | null>(null)

  const attach = useCallback((node: HTMLElement | null) => {
    observer.current?.disconnect()
    observer.current = null
    if (!node) return
    if (typeof IntersectionObserver === "undefined") {
      setStatus((s) => (s === "waiting" ? "playing" : s))
      return
    }
    const io = new IntersectionObserver(
      (entries) => {
        if (!entries.some((e) => e.isIntersecting)) return
        setStatus((s) => (s === "waiting" ? "playing" : s))
        io.disconnect()
      },
      { threshold: 0.4 }
    )
    io.observe(node)
    observer.current = io
  }, [])

  useEffect(() => () => observer.current?.disconnect(), [])

  // A new example is a user action: play it from the start right away.
  const lastKey = useRef(resetKey)
  useEffect(() => {
    if (Object.is(lastKey.current, resetKey)) return
    lastKey.current = resetKey
    setStep(0)
    setStatus("playing")
  }, [resetKey])

  useEffect(() => {
    if (!animate || status !== "playing") return
    const list = Array.isArray(durations) ? durations : [durations]
    const wait = list[Math.min(step, list.length - 1)]
    const timer = window.setTimeout(() => {
      if (step >= steps - 1) setStatus("done")
      else setStep(step + 1)
    }, wait)
    return () => window.clearTimeout(timer)
  }, [animate, status, step, steps, durations])

  const restart = useCallback(() => {
    setStep(0)
    setStatus("playing")
  }, [])

  const toggle = useCallback(() => {
    if (status === "done") restart()
    else setStatus(status === "playing" ? "paused" : "playing")
  }, [status, restart])

  const goTo = useCallback(
    (next: number) => {
      setStep(Math.max(0, Math.min(steps - 1, next)))
      setStatus("paused")
    },
    [steps]
  )

  return {
    attach,
    // Reduced motion still honours a step the user picked: showing it moves nothing.
    step: animate || status === "paused" ? Math.min(step, steps - 1) : steps - 1,
    steps,
    status: animate ? status : "done",
    animated: animate,
    toggle,
    goTo,
    restart,
  }
}
