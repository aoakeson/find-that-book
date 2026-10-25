import "@testing-library/jest-dom/vitest"
import { cleanup } from "@testing-library/react"
import { afterEach, vi } from "vitest"
import { historyStore } from "@/features/history/storage"

// jsdom gaps the app touches. Tests can override matchMedia per case (e.g. reduced motion).
window.scrollTo = vi.fn() as unknown as typeof window.scrollTo
if (!window.matchMedia) {
  window.matchMedia = (query: string) =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList
}
if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver
}

afterEach(() => {
  cleanup()
  window.localStorage.clear()
  // The store caches its state in a module singleton; without this, entries leak between tests in one file.
  historyStore.resetForTests()
  vi.unstubAllGlobals()
})
