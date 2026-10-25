import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  useSyncExternalStore,
  type ReactNode,
} from "react"

/**
 * Decorative motion (paper background drift, pointer parallax, the page-turn book, the activity shimmer) runs only
 * when the OS doesn't ask for reduced motion AND the user hasn't paused it with the in-app control.
 */
export interface Motion {
  /** The OS/browser `prefers-reduced-motion: reduce` setting, live. */
  reducedMotion: boolean
  /** The user's choice from the pause control, remembered on this browser. */
  paused: boolean
  /** True when decorative motion may run. */
  animate: boolean
  setPaused: (paused: boolean) => void
}

const PAUSED_KEY = "ftb.motionPaused"
export const REDUCED_MOTION_QUERY = "(prefers-reduced-motion: reduce)"

const MotionContext = createContext<Motion | null>(null)

export function MotionProvider({ children }: { children: ReactNode }) {
  const reducedMotion = useMediaQuery(REDUCED_MOTION_QUERY)
  const [paused, setPausedState] = useState(readPaused)

  const setPaused = useCallback((next: boolean) => {
    setPausedState(next)
    try {
      window.localStorage.setItem(PAUSED_KEY, next ? "1" : "0")
    } catch {
      // Storage unavailable: the choice lasts for this visit only.
    }
  }, [])

  // Lets plain CSS (e.g. the Trace shimmer) honour the pause without each component reading this context.
  useEffect(() => {
    const root = document.documentElement
    if (paused) root.dataset.motion = "paused"
    else delete root.dataset.motion
    return () => {
      delete root.dataset.motion
    }
  }, [paused])

  const value = useMemo<Motion>(
    () => ({ reducedMotion, paused, animate: !reducedMotion && !paused, setPaused }),
    [reducedMotion, paused, setPaused]
  )
  return <MotionContext.Provider value={value}>{children}</MotionContext.Provider>
}

/** Outside a provider (isolated component tests) motion still follows the OS setting and can't be paused. */
export function useMotion(): Motion {
  const context = useContext(MotionContext)
  const reducedMotion = useMediaQuery(REDUCED_MOTION_QUERY)
  return context ?? { reducedMotion, paused: false, animate: !reducedMotion, setPaused: noop }
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

function readPaused(): boolean {
  try {
    return window.localStorage.getItem(PAUSED_KEY) === "1"
  } catch {
    return false
  }
}

function noop() {}
