import { vi } from "vitest"
import { fixtures } from "./fixtures"

/**
 * A fetch stand-in for component tests. Routes are matched on method + pathname; each handler gets the URL and
 * init and returns a Response (use json()/problem() below). Unmatched requests fail the test loudly.
 * GET /api/health (asked by the home page: is AI on?) answers the API's example unless a test routes it itself;
 * it's free and stays out of `calls`, so "no API calls" assertions keep meaning searches and suggestions.
 */
export type Handler = (url: URL, init: RequestInit | undefined) => Response | Promise<Response>

export function fakeApi(routes: Record<string, Handler>) {
  const calls: { method: string; url: URL; body: unknown }[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(
      typeof input === "string" ? input : input instanceof URL ? input.href : input.url,
      "http://localhost"
    )
    const method = (init?.method ?? "GET").toUpperCase()
    const key = `${method} ${url.pathname}`
    if (key === "GET /api/health" && !routes[key]) return json(fixtures.health())
    calls.push({ method, url, body: init?.body ? JSON.parse(String(init.body)) : undefined })
    const handler = routes[key]
    if (!handler) throw new Error(`Unexpected request: ${method} ${url.pathname}${url.search}`)
    if (init?.signal?.aborted) throw new DOMException("Aborted", "AbortError")
    return handler(url, init)
  })
  vi.stubGlobal("fetch", fetchMock)
  return {
    fetch: fetchMock,
    calls,
    count: (method: string, path: string) => calls.filter((c) => c.method === method && c.url.pathname === path).length,
  }
}

export function json(body: unknown, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" }, ...init })
}

export function problem(body: { status?: number | null }, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status: body.status ?? 500,
    headers: { "Content-Type": "application/problem+json", ...headers },
  })
}

/**
 * Holds every request until the test answers it, and ignores aborts: like a response that was already on the wire
 * when the user moved on. `requests[i].signal` is what the client passed to fetch, so tests can assert it was aborted.
 */
export function manual() {
  const requests: {
    url: URL
    signal: AbortSignal | null
    respond: (r: Response) => void
    fail: (e: unknown) => void
  }[] = []
  const handler: Handler = (url, init) =>
    new Promise<Response>((respond, fail) => {
      requests.push({ url, signal: init?.signal ?? null, respond, fail })
    })
  return { handler, requests }
}

/** A response that only settles when the request is aborted or `resolve` is called: for cancel/stale tests. */
export function deferred() {
  let resolve!: (r: Response) => void
  const handler: Handler = (_url, init) =>
    new Promise<Response>((res, rej) => {
      resolve = res
      init?.signal?.addEventListener("abort", () => rej(new DOMException("Aborted", "AbortError")))
    })
  return { handler, resolve: (r: Response) => resolve(r) }
}
