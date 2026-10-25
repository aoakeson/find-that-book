import type { ApiError } from "@/api/client"

export interface FailureText {
  title: string
  description: string
  /** Seconds the server asked us to wait before trying again (429, 503). */
  waitSeconds: number | null
  /** A support reference (trace id), shown when the failure is ours to investigate. */
  reference: string | null
}

/** What a failed search says. Keyed on kind, code and status, never on the server's message text. */
export function describeFailure(
  error: ApiError,
  online = typeof navigator === "undefined" ? true : navigator.onLine
): FailureText {
  const base = { waitSeconds: null, reference: null }
  switch (error.kind) {
    case "network":
      return online
        ? {
            ...base,
            title: "Couldn't reach the search service",
            description: "The app got no answer from its server. Check your connection, then try again.",
          }
        : { ...base, title: "You're offline", description: "Reconnect to the internet, then try again." }
    case "timeout":
      return {
        ...base,
        title: "The search took too long",
        description: "No answer came back in time. Try again in a moment.",
      }
    case "unexpected":
      return {
        ...base,
        title: "The answer couldn't be read",
        description: "The server sent something this app doesn't understand. Try again in a moment.",
      }
    default:
      break
  }

  // Any status may carry Retry-After (429, 503, and 504 sends 5 s): honour it whatever the code.
  const wait = error.retryAfterSeconds !== null && error.retryAfterSeconds > 0 ? error.retryAfterSeconds : null
  return { ...httpFailure(error), waitSeconds: wait }
}

function httpFailure(error: ApiError): Omit<FailureText, "waitSeconds"> {
  const base = { reference: null }
  switch (error.code ?? "") {
    case "rate_limited":
      return {
        ...base,
        title: "Too many searches",
        description: "The app limits how many searches run each minute. Wait a moment, then try again.",
      }
    case "catalog_unavailable":
      return {
        ...base,
        title: "Open Library is unavailable",
        description: "Open Library isn't answering right now, so the search couldn't run. Try again shortly.",
      }
    case "catalog_bad_response":
      return {
        ...base,
        title: "Open Library sent an unusable answer",
        description: "This is usually brief. Try again in a moment.",
      }
    case "catalog_timeout":
      return { ...base, title: "Open Library didn't answer in time", description: "Try again in a moment." }
    case "search_timeout":
      return {
        ...base,
        title: "The search didn't finish in time",
        description: "No Open Library results arrived before the time limit. Try again in a moment.",
      }
    case "internal_error":
      return {
        title: "Something went wrong on the server",
        description: "Try again. If it keeps happening, report the reference below.",
        reference: error.traceId,
      }
    default:
      return {
        title: "The search didn't work",
        description: `The server answered with an error${error.status ? ` (${error.status})` : ""}. Try again in a moment.`,
        reference: error.traceId,
      }
  }
}
