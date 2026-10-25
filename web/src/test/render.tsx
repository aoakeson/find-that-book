import { act, render } from "@testing-library/react"
import { createMemoryHistory } from "@tanstack/react-router"
import { StrictMode } from "react"
import { App, createQueryClient } from "@/app/App"
import { createAppRouter } from "@/router"

/**
 * Renders the whole app (real router, real providers) at a URL. Pair with fakeApi() for the network.
 * `strict` wraps it in <StrictMode> like main.tsx does, for tests about duplicate effects and requests.
 */
export async function renderApp(url = "/", options: { strict?: boolean } = {}) {
  const history = createMemoryHistory({ initialEntries: [url] })
  const router = createAppRouter(history)
  const queryClient = createQueryClient()
  const app = <App router={router} queryClient={queryClient} />
  const result = render(options.strict ? <StrictMode>{app}</StrictMode> : app)
  await act(() => router.load())
  return { ...result, router, history, queryClient }
}
