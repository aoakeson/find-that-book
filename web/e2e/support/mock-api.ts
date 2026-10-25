import { readdirSync, readFileSync } from "node:fs"
import { resolve } from "node:path"
import { expect, test as base, type Page, type Route } from "@playwright/test"
import type {
  AdminAiStatus,
  HealthResponse,
  ProblemBody,
  SearchResponse,
  SuggestResponse,
} from "../../src/api/types.ts"

/*
 * Every /api request in these tests is answered here from the API's own example files, so the suite needs no backend,
 * no keys and makes no paid calls. A request nobody mocked gets a 501 and fails the test at teardown.
 */

const EXAMPLES_DIR = resolve(import.meta.dirname, "../../../src/FindThatBook.Api/OpenApi/Examples")
const EXAMPLE_FILES = readdirSync(EXAMPLES_DIR).filter((f) => f.endsWith(".json"))

/** The `value` of an API example, by file name without .json, or the first whose name matches a pattern. */
export function example<T>(name: string | RegExp): T {
  const file = EXAMPLE_FILES.find((f) => (typeof name === "string" ? f === `${name}.json` : name.test(f)))
  if (!file) throw new Error(`No API example ${name}`)
  return (JSON.parse(readFileSync(resolve(EXAMPLES_DIR, file), "utf8")) as { value: T }).value
}

export const examples = {
  match: () => example<SearchResponse>("SearchBooks.200.clear_match"),
  ambiguous: () => example<SearchResponse>("SearchBooks.200.ambiguous_author_only"),
  none: () => example<SearchResponse>("SearchBooks.200.none"),
  catalogUnavailable: () => example<ProblemBody>("SearchBooks.503.catalog_unavailable"),
  suggestions: () => example<SuggestResponse>(/^SuggestBooks\.200\.prefix_/),
  noSuggestions: () => example<SuggestResponse>("SuggestBooks.200.empty_prefix"),
  health: () => example<HealthResponse>("Health.200.ok"),
}

/** GET /api/admin/ai has no example file; the same shape as src/test/fixtures.ts adminStatus(), with fake fingerprints. */
export function adminStatus(): AdminAiStatus {
  return {
    active_provider: "gemini",
    provider_selection: "explicit",
    ai_configured: true,
    providers: [
      {
        name: "gemini",
        model: "gemini-3.5-flash-lite",
        active: true,
        configured: true,
        key_source: "dotenv",
        key_fingerprint: "sha256:1a2b3c4d",
      },
      {
        name: "anthropic",
        model: "claude-haiku-5-5",
        active: false,
        configured: false,
        key_source: "none",
        key_fingerprint: null,
      },
      {
        name: "openai",
        model: "gpt-4.1-mini",
        active: false,
        configured: true,
        key_source: "runtime",
        key_fingerprint: "sha256:9f8e7d6c",
      },
    ],
    judge: {
      enabled: false,
      model: "gpt-6-luna",
      configured: true,
      key_source: "environment",
      key_fingerprint: "sha256:0a1b2c3d",
      threshold: 0.9,
    },
    persistence: "memory (runtime changes are lost on restart)",
  }
}

export interface Reply {
  status?: number
  body?: unknown
  headers?: Record<string, string>
  /** Holds the answer back, e.g. to press Stop while a search runs. */
  delayMs?: number
}

export type Responder = (url: URL, body: unknown) => Reply | Promise<Reply>

export const problem = (body: ProblemBody, headers: Record<string, string> = {}): Reply => ({
  status: body.status ?? 500,
  body,
  headers: { "Content-Type": "application/problem+json", ...headers },
})

/** Answers a search with an example, echoing the searched text as `query` like the API does. */
export const answer =
  (make: () => SearchResponse): Responder =>
  (url) => ({ body: { ...make(), query: url.searchParams.get("q") ?? "" } })

export class MockApi {
  readonly requests: { method: string; url: URL; body: unknown }[] = []
  readonly unmocked: string[] = []
  private readonly routes = new Map<string, Responder>()
  /** Held-back answers, released (and dropped) when the test ends. */
  private readonly waiting = new Set<() => void>()
  private ended = false

  /** Mocks `METHOD /api/path` (exact pathname). A later call for the same route replaces the earlier one. */
  on(method: string, path: string, reply: Responder | Reply): this {
    this.routes.set(`${method} ${path}`, typeof reply === "function" ? reply : () => reply)
    return this
  }

  count(method: string, path: string): number {
    return this.requests.filter((r) => r.method === method && r.url.pathname === path).length
  }

  async handle(route: Route) {
    // After the test: never let a late request (a debounced lookup, a refetch) through to the dev server's proxy.
    if (this.ended) return route.abort().catch(() => {})
    const request = route.request()
    const url = new URL(request.url())
    const method = request.method()
    const body = request.postData() ? (JSON.parse(request.postData() ?? "null") as unknown) : undefined
    this.requests.push({ method, url, body })
    const responder = this.routes.get(`${method} ${url.pathname}`)
    if (!responder) {
      this.unmocked.push(`${method} ${url.pathname}${url.search}`)
      await route.fulfill({
        status: 501,
        contentType: "application/problem+json",
        body: JSON.stringify({ status: 501, code: "not_mocked" }),
      })
      return
    }
    const reply = await responder(url, body)
    if (reply.delayMs) await this.hold(reply.delayMs)
    if (this.ended) return route.abort().catch(() => {})
    // The page may have aborted the request meanwhile (Stop, a newer search): nothing left to answer then.
    await route
      .fulfill({
        status: reply.status ?? 200,
        headers: { "Content-Type": "application/json", ...reply.headers },
        body: JSON.stringify(reply.body ?? null),
      })
      .catch(() => {})
  }

  end() {
    this.ended = true
    this.waiting.forEach((release) => release())
  }

  private hold(ms: number) {
    return new Promise<void>((done) => {
      const release = () => {
        clearTimeout(timer)
        this.waiting.delete(release)
        done()
      }
      const timer = setTimeout(release, ms)
      this.waiting.add(release)
    })
  }
}

export const test = base.extend<{ api: MockApi }>({
  api: [
    // `provide` rather than Playwright's usual `use`: the React hooks lint rule would take `use(…)` for a hook.
    async ({ page }, provide) => {
      const api = new MockApi()
        .on("GET", "/api/suggest", { body: examples.noSuggestions() })
        .on("GET", "/api/health", { body: examples.health() })
      const pageErrors: string[] = []
      page.on("pageerror", (error) => pageErrors.push(error.message))
      await page.route(
        (url) => url.pathname.startsWith("/api/"),
        (route) => api.handle(route)
      )

      await provide(api)

      api.end()
      expect(api.unmocked, "every /api request must be mocked").toEqual([])
      expect(pageErrors, "uncaught errors in the page").toEqual([])
    },
    { auto: true },
  ],
})

export { expect }

export const composer = (page: Page) => page.getByRole("combobox", { name: "Describe the book" })

/** Types into the composer and presses Enter, like a user. */
export async function search(page: Page, text: string) {
  await composer(page).fill(text)
  await composer(page).press("Enter")
}

/** Pixels the page scrolls sideways (0 when nothing overflows). */
export function horizontalOverflow(page: Page): Promise<number> {
  return page.evaluate<number>("document.documentElement.scrollWidth - document.documentElement.clientWidth")
}
