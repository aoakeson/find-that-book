import { act, renderHook } from "@testing-library/react"
import type { ReactNode } from "react"
import { describe, expect, it, vi } from "vitest"
import { MotionProvider, REDUCED_MOTION_QUERY, useMotion } from "./motion"

/** A matchMedia whose reduced-motion answer the test controls, with working change listeners. */
function stubReducedMotion(initial: boolean) {
  let reduced = initial
  const listeners = new Set<() => void>()
  vi.stubGlobal("matchMedia", (query: string) => ({
    get matches() {
      return query === REDUCED_MOTION_QUERY ? reduced : false
    },
    media: query,
    onchange: null,
    addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
    removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  }))
  return {
    set(next: boolean) {
      reduced = next
      act(() => listeners.forEach((listener) => listener()))
    },
    listenerCount: () => listeners.size,
  }
}

const wrapper = ({ children }: { children: ReactNode }) => <MotionProvider>{children}</MotionProvider>

describe("MotionProvider", () => {
  it("animates by default", () => {
    stubReducedMotion(false)
    const { result } = renderHook(useMotion, { wrapper })
    expect(result.current).toMatchObject({ reducedMotion: false, animate: true })
  })

  it("follows the reduced-motion setting live and unsubscribes on unmount", () => {
    const media = stubReducedMotion(true)
    const { result, unmount } = renderHook(useMotion, { wrapper })
    expect(result.current).toMatchObject({ reducedMotion: true, animate: false })

    media.set(false)
    expect(result.current).toMatchObject({ reducedMotion: false, animate: true })

    unmount()
    expect(media.listenerCount()).toBe(0)
  })

  it("outside a provider, follows the OS setting", () => {
    stubReducedMotion(true)
    const { result } = renderHook(useMotion)
    expect(result.current).toMatchObject({ reducedMotion: true, animate: false })
  })
})
