import { act, render, renderHook, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { ReactNode } from "react"
import { afterEach, describe, expect, it, vi } from "vitest"
import { MotionToggle } from "@/app/MotionToggle"
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

afterEach(() => {
  delete document.documentElement.dataset.motion
})

describe("MotionProvider", () => {
  it("animates by default", () => {
    stubReducedMotion(false)
    const { result } = renderHook(useMotion, { wrapper })
    expect(result.current).toMatchObject({ reducedMotion: false, paused: false, animate: true })
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

  it("remembers a pause across visits and marks the document for CSS", () => {
    stubReducedMotion(false)
    const first = renderHook(useMotion, { wrapper })
    act(() => first.result.current.setPaused(true))
    expect(first.result.current).toMatchObject({ paused: true, animate: false })
    expect(document.documentElement.dataset.motion).toBe("paused")
    first.unmount()

    const second = renderHook(useMotion, { wrapper })
    expect(second.result.current.paused).toBe(true)

    act(() => second.result.current.setPaused(false))
    expect(document.documentElement.dataset.motion).toBeUndefined()
    expect(renderHook(useMotion, { wrapper }).result.current.paused).toBe(false)
  })

  it("still pauses for this visit when storage is unavailable", () => {
    stubReducedMotion(false)
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("denied", "SecurityError")
    })
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("full", "QuotaExceededError")
    })
    const { result } = renderHook(useMotion, { wrapper })
    expect(result.current.paused).toBe(false)
    act(() => result.current.setPaused(true))
    expect(result.current).toMatchObject({ paused: true, animate: false })
  })

  it("outside a provider, follows the OS setting and cannot be paused", () => {
    stubReducedMotion(true)
    const { result } = renderHook(useMotion)
    expect(result.current).toMatchObject({ reducedMotion: true, paused: false, animate: false })
  })
})

describe("MotionToggle", () => {
  it("is one toggle with a constant name whose pressed state is the pause", async () => {
    stubReducedMotion(false)
    render(<MotionToggle />, { wrapper })
    const toggle = screen.getByRole("button", { name: "Pause background motion" })
    expect(toggle).toHaveAttribute("aria-pressed", "false")

    await userEvent.click(toggle)
    expect(screen.getByRole("button", { name: "Pause background motion" })).toHaveAttribute("aria-pressed", "true")
    expect(window.localStorage.getItem("ftb.motionPaused")).toBe("1")
  })

  it("is not offered when the OS already reduces motion", () => {
    stubReducedMotion(true)
    render(<MotionToggle />, { wrapper })
    expect(screen.queryByRole("button", { name: "Pause background motion" })).not.toBeInTheDocument()
  })
})
