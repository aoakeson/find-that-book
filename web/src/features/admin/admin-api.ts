import { useMemo, useRef } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ApiError } from "@/api/client"
import { getAdminAi, updateAdminAi } from "@/api/endpoints"
import { HEALTH_QUERY_KEY } from "@/api/health"
import type { AdminAiStatus, AdminAiUpdate } from "@/api/types"

export const ADMIN_AI_QUERY_KEY = ["admin", "ai"] as const

/**
 * Every change answers with the whole status. Changes in one scope run one after another, so a slower earlier answer
 * can never land last and overwrite a newer one.
 */
export const ADMIN_CHANGE_SCOPE = { id: "admin-ai-change" }

/** GET /api/admin/ai: free, no provider call. Never retried on its own; the page offers "Try again". */
export function useAdminAiStatus() {
  return useQuery({
    queryKey: ADMIN_AI_QUERY_KEY,
    queryFn: ({ signal }) => getAdminAi(signal),
    retry: false,
    refetchOnWindowFocus: false,
  })
}

/**
 * Every admin change answers with the full status; it replaces the cached one so the page shows what the API now has.
 * Health is refreshed too, so the search page's "AI is off" note follows a key added here.
 */
export function useStoreStatus() {
  const queryClient = useQueryClient()
  return async (status: AdminAiStatus) => {
    // A status GET still in flight was started before this change; let it land late and it would undo the change.
    await queryClient.cancelQueries({ queryKey: ADMIN_AI_QUERY_KEY, exact: true })
    queryClient.setQueryData(ADMIN_AI_QUERY_KEY, status)
    void queryClient.invalidateQueries({ queryKey: HEALTH_QUERY_KEY })
  }
}

/** PUT /api/admin/ai for settings that are not secret (provider, judge). One instance per control, so each has its own state. */
export function useUpdateAdminAi() {
  const storeStatus = useStoreStatus()
  return useMutation<AdminAiStatus, ApiError, AdminAiUpdate>({
    mutationKey: [...ADMIN_AI_QUERY_KEY, "settings"],
    scope: ADMIN_CHANGE_SCOPE,
    mutationFn: (update) => updateAdminAi(update),
    onSuccess: storeStatus,
  })
}

export function isUnavailable(error: unknown): boolean {
  return error instanceof ApiError && error.kind === "http" && error.status === 404
}

/** Blocks a second start while one request is in flight: a double click or Enter + click sends one request. */
export function useInFlight() {
  const busy = useRef(false)
  return useMemo(
    () => ({
      start: () => {
        if (busy.current) return false
        busy.current = true
        return true
      },
      end: () => {
        busy.current = false
      },
    }),
    []
  )
}

/**
 * What to show next to a control when an admin call fails. Built from the error's kind, status and the API's own
 * field messages only: never from response text we don't control, and never from anything the user typed.
 */
export function adminErrorMessage(
  error: unknown,
  { field, fallback = "The API couldn't make this change. Try again." }: { field?: string; fallback?: string } = {}
): string {
  if (!(error instanceof ApiError)) return "Something went wrong. Try again."
  switch (error.kind) {
    case "network":
      return "Couldn't reach the API. Check that it's running, then try again."
    case "timeout":
      return "The API didn't answer in time. Try again."
    case "aborted":
      return "The request was cancelled."
    case "unexpected":
      return "The API sent a response this page can't read. Try again."
  }
  if (error.status === 400) {
    const fieldMessage = field ? error.fieldErrors(field)[0] : undefined
    return fieldMessage ?? Object.values(error.problem?.errors ?? {}).flat()[0] ?? "The API didn't accept this change."
  }
  if (error.status === 404)
    return "The admin API isn't available. It only answers in Development, on the API's own machine."
  if (error.status === 429) {
    const wait = error.retryAfterSeconds
    return wait
      ? `Too many requests in a row. Wait ${wait} seconds, then try again.`
      : "Too many requests in a row. Wait a moment, then try again."
  }
  return fallback
}
