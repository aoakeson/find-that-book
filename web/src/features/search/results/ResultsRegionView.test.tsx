import { act, fireEvent, render, screen, within } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { ApiError } from "@/api/client"
import type { SearchResponse } from "@/api/types"
import type { SearchView } from "@/features/search/session"
import { toSnapshot } from "@/features/history/snapshot"
import { fixtures } from "@/test/fixtures"
import { ResultsRegionView, type ResultsSession } from "./ResultsRegion"

/** The results area against a stub session: timers and outcomes are easier to drive here than through the app. */
function session(view: SearchView, overrides: Partial<ResultsSession> = {}): ResultsSession {
  return { view, retry: vi.fn(), focusComposer: vi.fn(), activityOpen: false, setActivityOpen: vi.fn(), ...overrides }
}

const success = (response: SearchResponse, runId = 2): SearchView => ({
  kind: "success",
  runId,
  query: response.query,
  response,
  snapshot: toSnapshot(response),
  entryId: `entry${runId}`,
})

const failed = (error: ApiError, runId = 2): SearchView => ({
  kind: "failed",
  runId,
  query: "tolkien hobbit",
  error,
  retryAt: error.retryAfterSeconds ? Date.now() + error.retryAfterSeconds * 1000 : null,
  previous: null,
})

const httpError = (status: number, code: string, retryAfterSeconds: number | null = null) =>
  new ApiError("http", code, { status, problem: { status, code, trace_id: "00-trace-01" }, retryAfterSeconds })

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

describe("waiting before trying again", () => {
  it.each([
    { status: 429, code: "rate_limited", title: "Too many searches", wait: 60 },
    { status: 503, code: "catalog_unavailable", title: "Open Library is unavailable", wait: 15 },
    { status: 504, code: "search_timeout", title: "The search didn't finish in time", wait: 5 },
    { status: 504, code: "catalog_timeout", title: "Open Library didn't answer in time", wait: 5 },
  ])(
    "$status $code keeps Try again disabled for Retry-After, then enables it without retrying",
    ({ status, code, title, wait }) => {
      const s = session(failed(httpError(status, code, wait)))
      render(<ResultsRegionView session={s} />)

      expect(screen.getByText(title)).toBeVisible()
      const button = screen.getByRole("button", { name: "Try again" })
      // Disabled but still focusable, so keyboard users can reach it and the countdown beside it.
      expect(button).toHaveAttribute("aria-disabled", "true")
      expect(button).not.toHaveAttribute("disabled")
      expect(screen.getByText(`You can try again in ${wait} s`)).toBeVisible()
      fireEvent.click(button)
      expect(s.retry).not.toHaveBeenCalled()

      act(() => vi.advanceTimersByTime((wait - 1) * 1000))
      expect(button).toHaveAttribute("aria-disabled", "true")
      expect(screen.getByText("You can try again in 1 s")).toBeVisible()

      act(() => vi.advanceTimersByTime(1000))
      expect(button).toHaveAttribute("aria-disabled", "false")
      expect(screen.queryByText(/You can try again in/)).not.toBeInTheDocument()
      act(() => vi.advanceTimersByTime(120_000))
      expect(s.retry).not.toHaveBeenCalled()

      fireEvent.click(button)
      expect(s.retry).toHaveBeenCalledTimes(1)
    }
  )

  it("doesn't restart the wait when the panel comes back (e.g. after a visit to Admin)", () => {
    const view = failed(httpError(429, "rate_limited", 15))
    const first = render(<ResultsRegionView session={session(view)} />)
    first.unmount()
    act(() => vi.advanceTimersByTime(20_000))

    render(<ResultsRegionView session={session(view)} />)
    expect(screen.getByRole("button", { name: "Try again" })).toHaveAttribute("aria-disabled", "false")
    expect(screen.queryByText(/You can try again in/)).not.toBeInTheDocument()
  })

  it("lets a rate-limited search be retried at once when the server gave no Retry-After", () => {
    render(<ResultsRegionView session={session(failed(httpError(429, "rate_limited")))} />)
    expect(screen.getByRole("button", { name: "Try again" })).toBeEnabled()
  })
})

describe("existing content after a failure", () => {
  it("keeps the previous answer below the error, labelled as previous", () => {
    const view: SearchView = {
      kind: "failed",
      runId: 3,
      query: "tolkien hobbit",
      error: new ApiError("network", "Could not reach the API."),
      retryAt: null,
      previous: { query: "dickens", snapshot: toSnapshot(fixtures.ambiguous()) },
    }
    render(<ResultsRegionView session={session(view)} />)
    expect(screen.getByRole("button", { name: "Try again" })).toBeVisible()
    const previous = screen.getByRole("region", { name: "Previous results for “dickens”" })
    expect(within(previous).getByRole("heading", { level: 2, name: "A few books fit your clues" })).toBeVisible()
    expect(within(previous).getAllByRole("heading", { level: 3 })).toHaveLength(fixtures.ambiguous().candidates.length)
  })
})

describe("failure wording", () => {
  it("says offline when the browser is offline", () => {
    vi.spyOn(navigator, "onLine", "get").mockReturnValue(false)
    render(<ResultsRegionView session={session(failed(new ApiError("network", "Could not reach the API.")))} />)
    expect(screen.getByText("You're offline")).toBeVisible()
    expect(screen.getByRole("button", { name: "Try again" })).toBeEnabled()
  })

  it.each([
    [new ApiError("timeout", "slow"), "The search took too long"],
    [new ApiError("unexpected", "html"), "The answer couldn't be read"],
    [httpError(404, "not_found"), "The search didn't work"],
  ])("%s", (error, title) => {
    render(<ResultsRegionView session={session(failed(error))} />)
    expect(screen.getByText(title)).toBeVisible()
    expect(screen.queryByText("No book matched")).not.toBeInTheDocument()
  })
})

describe("stopped searches", () => {
  it("is neutral, keeps the query and offers Search again", () => {
    const s = session({ kind: "cancelled", runId: 3, query: "tolkien hobbit" })
    render(<ResultsRegionView session={s} />)
    expect(screen.getByText("Search stopped")).toBeVisible()
    expect(screen.getByText(/“tolkien hobbit”/)).toBeVisible()
    expect(screen.queryByRole("button", { name: "Try again" })).not.toBeInTheDocument()
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole("button", { name: "Search again" }))
    expect(s.retry).toHaveBeenCalledTimes(1)
  })
})

describe("search activity", () => {
  it("counts seconds while running without announcing them, and shows no stages", () => {
    const view: SearchView = { kind: "pending", runId: 1, query: "tolkien hobbit", startedAt: Date.now() }
    render(<ResultsRegionView session={session(view)} />)
    act(() => vi.advanceTimersByTime(3000))
    expect(screen.getByText("3 s")).toHaveAttribute("aria-hidden", "true")
    expect(screen.getByRole("status")).toHaveTextContent(/^Searching…$/)
    expect(screen.queryByText("Understanding your clues")).not.toBeInTheDocument()
  })

  it("is the user's choice to open, and opens with what the response measured", () => {
    const response = fixtures.contributorMatch()
    response.meta.elapsed_ms = 2530
    response.meta.timings_ms = { clean: 0, interpret: 590, retrieve: 2190, rank: 1, explain: 310, judge: 260 }
    response.meta.open_library.steps = [
      { step: "broad", outcome: "ok", cache_hit: false, attempts: 1, elapsed_ms: 1205, results: 20, num_found: 312 },
      { step: "hypothesis", outcome: "ok", cache_hit: false, attempts: 2, elapsed_ms: 980, results: 14, num_found: 14 },
    ]
    response.meta.llm.steps.forEach((step) => (step.status = "ok"))
    Object.assign(response.meta.judge, {
      enabled: true,
      status: "agreed",
      pick: response.candidates[0].work_id,
      model: "judge-model",
    })
    response.candidates.forEach((c, i) => (c.explanation_source = i === 1 ? "template" : "llm"))
    const setActivityOpen = vi.fn()
    const { rerender } = render(<ResultsRegionView session={session(success(response), { setActivityOpen })} />)
    expect(screen.queryByText("Understanding your clues")).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole("button", { name: /Search activity/ }))
    expect(setActivityOpen).toHaveBeenCalledWith(true)

    rerender(<ResultsRegionView session={session(success(response), { activityOpen: true, setActivityOpen })} />)
    expect(screen.getByRole("button", { name: /Search activity/ })).toHaveTextContent("2.5 s")
    expect(screen.getByText("Searched the likely title and author")).toBeVisible()
    expect(screen.getByText("14 results, 980 ms, retried once")).toBeVisible()
    expect(screen.getByText("A second AI check agreed with the first result.")).toBeVisible()
    expect(screen.getByText(`1 of ${response.candidates.length} explanations uses standard wording.`)).toBeVisible()
    expect(screen.getByText("Second opinion model: judge-model")).toBeInTheDocument()
  })

  it("gives no cause when the AI answered but its reading wasn't used", () => {
    const response = fixtures.none()
    response.interpretation = { ...response.interpretation, source: "heuristic", title: null, author: null }
    response.meta.llm.steps = response.meta.llm.steps.map((step) => ({ ...step, status: "ok" }))
    render(<ResultsRegionView session={session(success(response), { activityOpen: true })} />)
    expect(screen.getByText("Used the words as typed.")).toBeVisible()
    expect(screen.queryByText(/so it used the words as typed/)).not.toBeInTheDocument()
    expect(screen.queryByText("Writing explanations")).not.toBeInTheDocument()
  })

  it("mentions slow empty answers that were asked again", () => {
    const response = fixtures.match()
    response.meta.open_library.slow_empty_requeries = 2
    render(<ResultsRegionView session={session(success(response), { activityOpen: true })} />)
    expect(screen.getByText("Asked again 2 times after slow empty answers.")).toBeVisible()
  })

  it("shows a failed catalog call as a warning inside a successful search", () => {
    const response = fixtures.match()
    response.meta.open_library.steps.push({
      step: "any_of",
      outcome: "failed",
      cache_hit: false,
      attempts: 3,
      elapsed_ms: 4100,
      results: null,
      num_found: null,
    })
    render(<ResultsRegionView session={session(success(response), { activityOpen: true })} />)
    expect(screen.getByText("Tried a broader search")).toBeVisible()
    expect(screen.getByText("Open Library didn't answer, 4.1 s, retried 2 times")).toBeVisible()
    expect(screen.getByRole("heading", { name: "Best match" })).toBeVisible()
  })
})

describe("live region", () => {
  const settle = () => act(() => vi.advanceTimersByTime(200))

  it("announces each start and outcome once, in plain words", () => {
    const pending: SearchView = { kind: "pending", runId: 1, query: "q", startedAt: Date.now() }
    const { rerender } = render(<ResultsRegionView session={session(pending)} />)
    const status = screen.getByRole("status")
    settle()
    expect(status).toHaveTextContent("Searching…")

    const match = fixtures.match()
    const ambiguous = fixtures.ambiguous()
    const single = fixtures.degraded()
    single.candidates = single.candidates.slice(0, 1)
    const outcomes: [SearchView, string][] = [
      [success(match, 1), `Best match: ${match.candidates[0].title} by ${match.candidates[0].author}`],
      [success(ambiguous, 2), `${ambiguous.candidates.length} books fit your clues`],
      [success(single, 3), "1 book fits your clues"],
      [success(fixtures.none(), 4), "No book matched"],
      [failed(httpError(503, "catalog_unavailable"), 5), "Open Library is unavailable"],
      [{ kind: "cancelled", runId: 6, query: "q" }, "Search stopped"],
      [
        {
          kind: "restored",
          entry: { id: "abcdef1", query: "q", savedAt: new Date().toISOString(), result: toSnapshot(fixtures.match()) },
        },
        `Showing a saved search. Best match: ${match.candidates[0].title} by ${match.candidates[0].author}`,
      ],
      [
        {
          kind: "restored",
          entry: { id: "abcdef2", query: "q", savedAt: new Date().toISOString(), result: toSnapshot(fixtures.none()) },
        },
        "Showing a saved search. No book matched",
      ],
    ]
    for (const [view, message] of outcomes) {
      rerender(<ResultsRegionView session={session(view)} />)
      expect(status).toHaveTextContent(/^$/)
      settle()
      expect(status).toHaveTextContent(message)
    }
  })

  it("stays silent when the session puts the previous answer back after an invalid query", () => {
    const shown = success(fixtures.match(), 1)
    const { rerender } = render(<ResultsRegionView session={session(shown)} />)
    settle()
    expect(screen.getByRole("status")).toHaveTextContent(/^Best match/)

    rerender(
      <ResultsRegionView session={session({ kind: "pending", runId: 2, query: "the", startedAt: Date.now() })} />
    )
    settle()
    rerender(<ResultsRegionView session={session(shown)} />)
    settle()
    expect(screen.getByRole("status")).toHaveTextContent(/^$/)
  })

  it("announces a saved search each time it is opened", () => {
    const entry = { id: "abcdef1", query: "q", savedAt: new Date().toISOString(), result: toSnapshot(fixtures.match()) }
    const { rerender } = render(<ResultsRegionView session={session({ kind: "restored", entry })} />)
    settle()
    expect(screen.getByRole("status")).toHaveTextContent("Showing a saved search")
    rerender(<ResultsRegionView session={session({ kind: "pending", runId: 3, query: "q", startedAt: Date.now() })} />)
    settle()
    rerender(<ResultsRegionView session={session({ kind: "restored", entry })} />)
    settle()
    expect(screen.getByRole("status")).toHaveTextContent("Showing a saved search")
  })

  it("repeats “Searching…” for a new search with the same words", () => {
    const { rerender } = render(
      <ResultsRegionView session={session({ kind: "pending", runId: 1, query: "q", startedAt: Date.now() })} />
    )
    settle()
    rerender(<ResultsRegionView session={session({ kind: "pending", runId: 2, query: "q", startedAt: Date.now() })} />)
    expect(screen.getByRole("status")).toHaveTextContent(/^$/)
    settle()
    expect(screen.getByRole("status")).toHaveTextContent("Searching…")
  })
})
