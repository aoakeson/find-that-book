import { useMemo, useRef, type CompositionEvent, type KeyboardEvent } from "react"

/**
 * Tells whether a key press belongs to an IME composition (Japanese, Chinese, Korean input…), whose Enter confirms
 * the candidate text and must never submit or accept a suggestion.
 * Chrome and Firefox flag that keydown with `isComposing` (keyCode 229). Safari fires `compositionend` first and then
 * a keydown with `isComposing: false`, so a keydown right after a composition ended counts as part of it too.
 */
export function useImeGuard() {
  const composing = useRef(false)
  const endedAt = useRef(Number.NEGATIVE_INFINITY)

  return useMemo(
    () => ({
      onCompositionStart() {
        composing.current = true
      },
      onCompositionEnd(event: CompositionEvent) {
        composing.current = false
        endedAt.current = event.timeStamp
      },
      isComposing(event: KeyboardEvent): boolean {
        return (
          composing.current ||
          event.nativeEvent.isComposing ||
          event.keyCode === 229 ||
          event.timeStamp - endedAt.current < SAFARI_COMPOSITION_END_MS
        )
      },
    }),
    []
  )
}

/** Safari's trailing keydown arrives in the same task as `compositionend`; this window is far below human speed. */
const SAFARI_COMPOSITION_END_MS = 50
