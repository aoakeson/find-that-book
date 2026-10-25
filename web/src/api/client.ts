import type { ProblemBody } from "./types"

/**
 * Every failure the UI can see, normalised:
 * - aborted: the caller cancelled (a neutral outcome, never shown as an error)
 * - network: no HTTP response at all (offline, API not running, DNS…)
 * - timeout: the browser-side safety timeout fired
 * - http: the API answered with an error status (problem+json when it's ours)
 * - unexpected: a 2xx we could not read (e.g. an HTML page from a misconfigured proxy)
 */
export type ApiErrorKind = "aborted" | "network" | "timeout" | "http" | "unexpected"

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number | null
  readonly code: string | null
  readonly problem: ProblemBody | null
  readonly retryAfterSeconds: number | null

  constructor(
    kind: ApiErrorKind,
    message: string,
    options: {
      status?: number | null
      problem?: ProblemBody | null
      retryAfterSeconds?: number | null
      cause?: unknown
    } = {}
  ) {
    super(message, { cause: options.cause })
    this.name = "ApiError"
    this.kind = kind
    this.status = options.status ?? null
    this.problem = options.problem ?? null
    this.code = options.problem?.code ?? null
    this.retryAfterSeconds = options.retryAfterSeconds ?? null
  }

  get traceId(): string | null {
    return this.problem?.trace_id ?? null
  }

  /** Field messages keyed by the API's member names (Q, Limit, ApiKey…). */
  fieldErrors(field: string): string[] {
    return this.problem?.errors?.[field] ?? []
  }
}

/** Retry-After is either whole seconds or an HTTP date. */
export function parseRetryAfter(value: string | null, now = Date.now()): number | null {
  if (!value) return null
  const trimmed = value.trim()
  if (/^\d+$/.test(trimmed)) return Number(trimmed)
  const date = Date.parse(trimmed)
  if (Number.isNaN(date)) return null
  return Math.max(0, Math.ceil((date - now) / 1000))
}

interface RequestOptions {
  method?: "GET" | "PUT" | "POST" | "DELETE"
  body?: unknown
  signal?: AbortSignal
  /** Browser-side safety net only; the API enforces its own deadlines. */
  timeoutMs?: number
}

const DEFAULT_TIMEOUT_MS = 40_000

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, signal, timeoutMs = DEFAULT_TIMEOUT_MS } = options
  // One signal covers the headers and the body, so Stop and the timeout also end a stalled download.
  const timeout = new AbortController()
  const timer = setTimeout(() => timeout.abort(), timeoutMs)
  const onCallerAbort = () => timeout.abort()
  signal?.addEventListener("abort", onCallerAbort, { once: true })

  const interrupted = (cause: unknown): ApiError | null => {
    if (signal?.aborted) return new ApiError("aborted", "The request was cancelled.", { cause })
    if (timeout.signal.aborted) return new ApiError("timeout", "The API did not answer in time.", { cause })
    return null
  }
  const readJson = async <R>(response: Response): Promise<R | null> => {
    try {
      return (await response.json()) as R
    } catch (error) {
      const stopped = interrupted(error)
      if (stopped) throw stopped
      return null
    }
  }

  try {
    let response: Response
    try {
      if (signal?.aborted) throw new DOMException("Aborted", "AbortError")
      response = await fetch(path, {
        method,
        signal: timeout.signal,
        headers:
          body === undefined
            ? { Accept: "application/json" }
            : { Accept: "application/json", "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
        credentials: "same-origin",
      })
    } catch (error) {
      throw interrupted(error) ?? new ApiError("network", "Could not reach the Find That Book API.", { cause: error })
    }

    const isJson = /\bjson\b/i.test(response.headers.get("Content-Type") ?? "")

    if (!response.ok) {
      const problem = isJson ? await readJson<ProblemBody>(response) : null
      throw new ApiError("http", problem?.title ?? `The API answered ${response.status}.`, {
        status: response.status,
        problem,
        retryAfterSeconds: parseRetryAfter(response.headers.get("Retry-After")),
      })
    }

    if (!isJson)
      throw new ApiError("unexpected", "The API sent a response that isn't JSON.", { status: response.status })
    const data = await readJson<T>(response)
    if (data === null)
      throw new ApiError("unexpected", "The API sent JSON that could not be read.", { status: response.status })
    return data
  } finally {
    clearTimeout(timer)
    signal?.removeEventListener("abort", onCallerAbort)
  }
}
