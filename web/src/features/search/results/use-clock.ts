import { useEffect, useState } from "react"

/** Whole seconds since `startedAt`, ticking once a second. Display only: nothing announces it. */
export function useElapsedSeconds(startedAt: number): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])
  return Math.max(0, Math.floor((now - startedAt) / 1000))
}

/**
 * Whole seconds left until `deadline` (epoch ms), counting down to 0. The deadline is fixed when the failure arrives,
 * so leaving the page and coming back doesn't restart the wait, and a throttled background tab still ends on time.
 */
export function useCountdown(deadline: number | null): number {
  const left = () => (deadline === null ? 0 : Math.max(0, Math.ceil((deadline - Date.now()) / 1000)))
  const [remaining, setRemaining] = useState(left)
  const running = remaining > 0
  useEffect(() => {
    if (!running || deadline === null) return
    const timer = setInterval(() => setRemaining(Math.max(0, Math.ceil((deadline - Date.now()) / 1000))), 250)
    return () => clearInterval(timer)
  }, [deadline, running])
  return remaining
}
