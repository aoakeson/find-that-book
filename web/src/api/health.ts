import { useQuery } from "@tanstack/react-query"
import { getHealth } from "./endpoints"

export const HEALTH_QUERY_KEY = ["health"] as const

/** GET /api/health: free (no Open Library or LLM call). Admin changes invalidate it, so "AI is off" notes stay true. */
export function useHealth() {
  return useQuery({
    queryKey: HEALTH_QUERY_KEY,
    queryFn: ({ signal }) => getHealth(signal),
    staleTime: 30_000,
  })
}
