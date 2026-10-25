import { useRouter } from "@tanstack/react-router"
import { useEffect } from "react"

/**
 * After a client-side move to another page (pathname change; not search-param changes, not the first load), puts
 * focus on the new page's `#page-heading`, or `#main`, so screen readers announce the page. Leaves focus alone if
 * something inside main already took it (e.g. "New search" focusing the composer).
 */
export function useRouteFocus() {
  const router = useRouter()
  useEffect(() => {
    let frame = 0
    const unsubscribe = router.subscribe("onResolved", (event) => {
      if (!event.fromLocation || !event.pathChanged) return
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(() => {
        const main = document.getElementById("main")
        const active = document.activeElement
        if (main && active && active !== main && main.contains(active)) return
        const target = document.getElementById("page-heading") ?? main
        target?.focus({ preventScroll: true })
      })
    })
    return () => {
      cancelAnimationFrame(frame)
      unsubscribe()
    }
  }, [router])
}
