import { describe, expect, it, vi } from "vitest"
import { fixtures } from "@/test/fixtures"
import { deferred, fakeApi, json, problem } from "@/test/fake-api"
import { ApiError, apiRequest, parseRetryAfter } from "./client"
import { searchBooks } from "./endpoints"

describe("api client", () => {
  it("returns the parsed body for a 200", async () => {
    fakeApi({ "GET /api/books/search": () => json(fixtures.match()) })
    const result = await searchBooks("tolkien hobbit")
    expect(result.status).toBe("match")
  })

  it("turns problem+json into an ApiError with code, trace id and Retry-After", async () => {
    fakeApi({ "GET /api/books/search": () => problem(fixtures.rateLimited(), { "Retry-After": "42" }) })
    const error = await searchBooks("x").catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ kind: "http", status: 429, code: "rate_limited", retryAfterSeconds: 42 })
    expect((error as ApiError).traceId).toBeTruthy()
  })

  it("reports a network failure separately from an HTTP error", async () => {
    fakeApi({ "GET /api/books/search": () => Promise.reject(new TypeError("Failed to fetch")) })
    await expect(searchBooks("x")).rejects.toMatchObject({ kind: "network" })
  })

  it("reports a caller abort as aborted, not as a failure", async () => {
    fakeApi({ "GET /api/books/search": () => json(fixtures.match()) })
    const controller = new AbortController()
    controller.abort()
    await expect(searchBooks("x", controller.signal)).rejects.toMatchObject({ kind: "aborted" })
  })

  it("never treats an HTML page as data", async () => {
    fakeApi({
      "GET /api/books/search": () => new Response("<!doctype html>", { headers: { "Content-Type": "text/html" } }),
    })
    await expect(searchBooks("x")).rejects.toMatchObject({ kind: "unexpected" })
  })

  it.each([
    ["42", 42],
    ["0", 0],
    [null, null],
    ["soon", null],
  ])("parses Retry-After %s", (value, expected) => {
    expect(parseRetryAfter(value)).toBe(expected)
  })

  it("reads Retry-After as an HTTP date, rounding up, and never below zero", () => {
    const now = Date.parse("2026-10-25T12:00:00Z")
    expect(parseRetryAfter("Sun, 25 Oct 2026 12:00:30 GMT", now)).toBe(30)
    expect(parseRetryAfter("Sun, 25 Oct 2026 12:00:00 GMT", now + 400)).toBe(0)
    expect(parseRetryAfter("Sun, 25 Oct 2026 11:59:00 GMT", now)).toBe(0)
  })

  it("reports its own safety timeout as timeout, not as a cancel or a network failure", async () => {
    fakeApi({ "GET /api/slow": deferred().handler })
    await expect(apiRequest("/api/slow", { timeoutMs: 20 })).rejects.toMatchObject({ kind: "timeout" })
  })

  it("reports a cancel during the request as aborted", async () => {
    fakeApi({ "GET /api/slow": deferred().handler })
    const controller = new AbortController()
    const request = apiRequest("/api/slow", { signal: controller.signal })
    controller.abort()
    await expect(request).rejects.toMatchObject({ kind: "aborted" })
  })

  it("keeps field messages from a validation problem, keyed by the API's member names", async () => {
    fakeApi({ "GET /api/books/search": () => problem(fixtures.validationFailed()) })
    const error = (await searchBooks("x").catch((e: unknown) => e)) as ApiError
    expect(error).toMatchObject({ kind: "http", status: 400, code: "validation_failed" })
    expect(error.fieldErrors("Q")).toEqual(["The query must be at most 300 characters."])
    expect(error.fieldErrors("Prefix")).toEqual([])
  })

  it("treats an error status without problem+json as http with no code", async () => {
    fakeApi({
      "GET /api/books/search": () =>
        new Response("<h1>Bad gateway</h1>", { status: 502, headers: { "Content-Type": "text/html" } }),
    })
    await expect(searchBooks("x")).rejects.toMatchObject({ kind: "http", status: 502, code: null, problem: null })
  })
})

describe("api client body reading", () => {
  it("lets Stop end a response whose body stalls after the headers", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
        const stream = new ReadableStream({
          start(controller) {
            init?.signal?.addEventListener("abort", () => controller.error(new DOMException("Aborted", "AbortError")))
          },
        })
        return new Response(stream, { headers: { "Content-Type": "application/json" } })
      })
    )
    const controller = new AbortController()
    const pending = searchBooks("x", controller.signal)
    await new Promise((resolve) => setTimeout(resolve, 0)) // headers are in; the body never comes
    controller.abort()
    await expect(pending).rejects.toMatchObject({ kind: "aborted" })
  })
})
