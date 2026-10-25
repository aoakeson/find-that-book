import { apiRequest } from "./client"
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

/** No `limit`: the API's default (5) is the brief's "up to 5 ordered candidates". */
export function searchBooks(query: string, signal?: AbortSignal): Promise<SearchResponse> {
  return apiRequest<SearchResponse>(`/api/books/search?${new URLSearchParams({ q: query })}`, { signal })
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
