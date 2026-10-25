import { useEffect, useRef, useState } from "react"
import type { Announcement } from "./announcement"

/** Long enough for screen readers to register the (empty) region before its text changes. */
const SETTLE_MS = 150
const REMEMBERED_KEYS = 50

/**
 * The page's one polite live region for search progress and outcomes. Each announcement key speaks once, ever: a view
 * that comes back (the previous answer after the API rejects a new query as invalid) stays silent. A new key first
 * clears the text, so the same words for a new search ("Searching…") are read again.
 */
export function Announcer({ announcement }: { announcement: Announcement | null }) {
  const [spoken, setSpoken] = useState<Announcement | null>(null)
  const announced = useRef<string[]>([])
  const key = announcement?.key
  const message = announcement?.message

  useEffect(() => {
    if (key === undefined || message === undefined || announced.current.includes(key)) return
    const timer = setTimeout(() => {
      announced.current = [...announced.current.slice(-(REMEMBERED_KEYS - 1)), key]
      setSpoken({ key, message })
    }, SETTLE_MS)
    return () => clearTimeout(timer)
  }, [key, message])

  const text = spoken && spoken.key === key ? spoken.message : ""
  return (
    <div role="status" aria-live="polite" aria-atomic="true" className="sr-only">
      {text}
    </div>
  )
}
