import { ApiError, apiRequest } from "./client"
import type {
  AdminAiStatus,
  AdminAiUpdate,
  AdminProbeResult,
  ChatProvider,
  HealthResponse,
  KeyName,
  SearchResponse,
  SuggestResponse,
} from "./types"

/**
 * The API's maximum. The results list shows the brief's "up to 5 ordered candidates" first and reveals the rest from
 * this same answer, because a second search would spend another LLM call and could read the query differently.
 */
export const SEARCH_LIMIT = 10

export async function searchBooks(query: string, signal?: AbortSignal): Promise<SearchResponse> {
  const params = new URLSearchParams({ q: query, limit: String(SEARCH_LIMIT) })
  const response = await apiRequest<unknown>(`/api/books/search?${params}`, { signal })
  if (!isSearchResponse(response)) throw new ApiError("unexpected", "The API sent an invalid search response.")
  return response
}

const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value)

function isSearchResponse(value: unknown): value is SearchResponse {
  if (!record(value) || !record(value.interpretation) || !Array.isArray(value.candidates)) return false
  if (
    typeof value.query !== "string" ||
    !["match", "ambiguous", "none"].includes(String(value.status)) ||
    !(value.hint === null || typeof value.hint === "string") ||
    !Array.isArray(value.degraded_reasons) ||
    !value.degraded_reasons.every((reason: unknown) => typeof reason === "string") ||
    !["book", "topic"].includes(String(value.interpretation.intent)) ||
    !Array.isArray(value.interpretation.topics) ||
    !value.interpretation.topics.every((topic: unknown) => typeof topic === "string")
  )
    return false
  return value.candidates.every(
    (candidate: unknown) =>
      record(candidate) &&
      typeof candidate.rank === "number" &&
      typeof candidate.work_id === "string" &&
      typeof candidate.title === "string" &&
      (candidate.author === null || typeof candidate.author === "string") &&
      Array.isArray(candidate.primary_authors) &&
      candidate.primary_authors.every((author: unknown) => record(author) && typeof author.name === "string") &&
      Array.isArray(candidate.contributors) &&
      candidate.contributors.every((credit: unknown) => record(credit) && typeof credit.name === "string") &&
      Array.isArray(candidate.matched_subjects) &&
      candidate.matched_subjects.every((subject: unknown) => typeof subject === "string") &&
      typeof candidate.explanation === "string"
  )
}

export const SUGGEST_LIMIT = 6

export function suggestBooks(prefix: string, signal?: AbortSignal): Promise<SuggestResponse> {
  const params = new URLSearchParams({ prefix, limit: String(SUGGEST_LIMIT) })
  return apiRequest<SuggestResponse>(`/api/suggest?${params}`, { signal, timeoutMs: 8_000 })
}

export function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  return apiRequest<HealthResponse>("/api/health", { signal, timeoutMs: 8_000 })
}

export function getAdminAi(signal?: AbortSignal): Promise<AdminAiStatus> {
  return apiRequest<AdminAiStatus>("/api/admin/ai", { signal, timeoutMs: 10_000 })
}

export function updateAdminAi(update: AdminAiUpdate): Promise<AdminAiStatus> {
  return apiRequest<AdminAiStatus>("/api/admin/ai", { method: "PUT", body: update, timeoutMs: 10_000 })
}

/** The key travels in this one request body and is not kept anywhere else by this module. */
export function setAdminKey(name: KeyName, apiKey: string): Promise<AdminAiStatus> {
  return apiRequest<AdminAiStatus>(`/api/admin/ai/keys/${name}`, {
    method: "PUT",
    body: { api_key: apiKey },
    timeoutMs: 10_000,
  })
}

export function clearAdminKey(name: KeyName): Promise<AdminAiStatus> {
  return apiRequest<AdminAiStatus>(`/api/admin/ai/keys/${name}`, { method: "DELETE", timeoutMs: 10_000 })
}

/** One tiny real provider call (costs a few tokens). */
export function probeProvider(provider: ChatProvider, signal?: AbortSignal): Promise<AdminProbeResult> {
  return apiRequest<AdminProbeResult>(`/api/admin/ai/probe/${provider}`, { method: "POST", signal, timeoutMs: 30_000 })
}
