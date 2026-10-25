import { useEffect, useState } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { suggestBooks } from "@/api/endpoints"
import type { SuggestResponse, SuggestionItem } from "@/api/types"

// docs/API.md suggests ~150 ms: long enough to skip most keystrokes, short enough to feel immediate.
const DEBOUNCE_MS = 150
/** The API's prefix limit (longer text gets a 400); past that the user is describing, not completing. */
const MAX_PREFIX_LENGTH = 100
/** The API sends Cache-Control: max-age=3600 for suggestions. */
const STALE_MS = 60 * 60 * 1000

const NONE: SuggestionItem[] = []

const suggestKey = (prefix: string) => ["suggest", prefix] as const

function canSuggest(prefix: string): boolean {
  return prefix.length > 0 && prefix.length <= MAX_PREFIX_LENGTH
}

export interface Suggestions {
  items: SuggestionItem[]
  /** The items are the last list shown, kept on screen while the answer for the current text is on its way. */
  stale: boolean
}

/**
 * Suggestions for the text in the composer. While the answer for the current text is pending, the list already on
 * screen stays (marked stale) instead of blinking away; the current text's answer then replaces it, and an empty or
 * failed answer clears it. Each prefix is its own query and superseded requests are aborted, so a late answer for an
 * older prefix is never applied. A failed lookup is just "no suggestions": it never gets in the way of searching.
 * `active` is false while the list is dismissed: no requests, nothing kept.
 */
export function useSuggestions(text: string, active: boolean): Suggestions {
  const prefix = text.trim()
  const debounced = useDebouncedValue(prefix, DEBOUNCE_MS)
  const queryClient = useQueryClient()
  const settled = debounced === prefix
  const query = useQuery({
    queryKey: suggestKey(debounced),
    queryFn: ({ signal }) => suggestBooks(debounced, signal),
    // Only once typing has paused on the text that is in the box now (never for a value it has already left).
    enabled: active && settled && canSuggest(prefix),
    staleTime: STALE_MS,
    retry: false,
  })

  // The answer for exactly this text, if there is one yet (a cached prefix shows at once, e.g. after Backspace).
  let answer: SuggestionItem[] | undefined
  if (!active || !canSuggest(prefix)) answer = NONE
  else if (!settled) answer = queryClient.getQueryData<SuggestResponse>(suggestKey(prefix))?.suggestions
  else if (query.data) answer = query.data.suggestions
  else if (query.isError) answer = NONE

  const [kept, setKept] = useState(NONE)
  if (answer !== undefined && answer !== kept) setKept(answer)

  return answer !== undefined ? { items: answer, stale: false } : { items: kept, stale: kept.length > 0 }
}

function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}
