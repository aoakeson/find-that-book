import { createContext, useCallback, useContext, useMemo, useSyncExternalStore, type ReactNode } from "react"

/**
 * Decorative motion (paper background drift, pointer parallax, the page-turn book, the activity shimmer) runs only
 * when the OS doesn't ask for reduced motion.
 */
export interface Motion {
  /** The OS/browser `prefers-reduced-motion: reduce` setting, live. */
  reducedMotion: boolean
  /** True when decorative motion may run. */
  animate: boolean
}

export const REDUCED_MOTION_QUERY = "(prefers-reduced-motion: reduce)"

const MotionContext = createContext<Motion | null>(null)

export function MotionProvider({ children }: { children: ReactNode }) {
  const reducedMotion = useMediaQuery(REDUCED_MOTION_QUERY)
  const value = useMemo<Motion>(() => ({ reducedMotion, animate: !reducedMotion }), [reducedMotion])
  return <MotionContext.Provider value={value}>{children}</MotionContext.Provider>
}

/** Outside a provider (isolated component tests) motion still follows the OS setting. */
export function useMotion(): Motion {
  const context = useContext(MotionContext)
  const reducedMotion = useMediaQuery(REDUCED_MOTION_QUERY)
  return context ?? { reducedMotion, animate: !reducedMotion }
}

/** Live `matchMedia` result; false where matchMedia is missing. */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (onChange: () => void) => {
      const list = mediaQueryList(query)
      if (!list) return noop
      if (typeof list.addEventListener === "function") {
        list.addEventListener("change", onChange)
        return () => list.removeEventListener("change", onChange)
      }
      list.addListener?.(onChange) // Safari < 14
      return () => list.removeListener?.(onChange)
    },
    [query]
  )
  return useSyncExternalStore(
    subscribe,
    () => mediaQueryList(query)?.matches ?? false,
    () => false
  )
}

function mediaQueryList(query: string): MediaQueryList | null {
  return typeof window !== "undefined" && typeof window.matchMedia === "function" ? window.matchMedia(query) : null
}

function noop() {}
