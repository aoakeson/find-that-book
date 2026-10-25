import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type FormEvent, type RefObject } from "react"
import { useSearch } from "@tanstack/react-router"
import { CircleAlertIcon, InfoIcon, SearchIcon, SquareIcon } from "lucide-react"
import type { SuggestionItem } from "@/api/types"
import {
  Autocomplete,
  AutocompleteContent,
  AutocompleteInput,
  AutocompleteInputGroup,
  AutocompleteList,
} from "@/components/ui/autocomplete"
import { Button } from "@/components/ui/button"
import { useSearchSession } from "@/features/search/session"
import { cn } from "@/lib/utils"
import { SuggestionOption, suggestionText } from "./SuggestionOption"
import { useImeGuard } from "./use-ime-guard"
import { useSuggestions } from "./use-suggestions"

const PLACEHOLDER = "A title, an author, or just a little detail…"

/**
 * The one search field. Typing suggests real works (GET /api/suggest only); accepting a suggestion fills the field
 * and never searches by itself. Enter with no suggestion highlighted, or the Search button, runs the search.
 */
export function Composer({ compact }: { compact: boolean }) {
  const { draft, setDraft, fieldError, pausedUntil, view, submit, cancel, registerComposer } = useSearchSession()
  // Whether the user wants the list: opened by typing or ArrowDown, closed by Escape, accepting, submitting or leaving.
  const [suggesting, setSuggesting] = useState(false)
  const suggestions = useSuggestions(draft, suggesting)
  const ime = useImeGuard()
  const inputRef = useRef<HTMLInputElement | null>(null)
  const inputId = useId()
  const labelId = useId()
  const errorId = useId()
  const pending = view.kind === "pending"

  const setInput = useCallback(
    (input: HTMLInputElement | null) => {
      inputRef.current = input
      registerComposer(input)
    },
    [registerComposer]
  )

  useInitialFocus(inputRef, !compact)
  const action = useActionFocusHandoff(pending)

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSuggesting(false)
    const willSearch = draft.trim().length > 0
    submit()
    // Phones: drop the keyboard so the results (or the search activity) are visible.
    if (willSearch && matchesMedia("(pointer: coarse)")) inputRef.current?.blur()
  }

  return (
    <form role="search" onSubmit={handleSubmit} data-compact={compact} className="w-full min-w-0">
      {/* Also referenced by aria-labelledby: while the list is open Base UI hides the rest of the page (this label
          included) from assistive tech, and only an aria-labelledby reference keeps a hidden label in the name. */}
      <label id={labelId} htmlFor={inputId} className="sr-only">
        Describe the book
      </label>
      <Autocomplete<SuggestionItem>
        value={draft}
        onValueChange={(value, details) => {
          // Base UI clears the field on Escape once the list is closed; here Escape never changes the text.
          if (details.reason === "escape-key") {
            details.cancel()
            setSuggesting(false)
            return
          }
          setDraft(value)
        }}
        open={suggesting && suggestions.items.length > 0}
        onOpenChange={setSuggesting}
        items={suggestions.items}
        filter={null}
        mode="list"
        itemToStringValue={suggestionText}
      >
        <AutocompleteInputGroup
          data-composer=""
          className={cn(
            "h-auto rounded-composer bg-surface shadow-composer",
            COMPOSER_EDGE,
            compact ? "gap-1.5 p-1.5 pl-2" : "gap-2 p-2 pl-3 sm:pl-4"
          )}
        >
          <AutocompleteInput
            ref={setInput}
            id={inputId}
            aria-labelledby={labelId}
            placeholder={PLACEHOLDER}
            enterKeyHint="search"
            aria-invalid={fieldError ? true : undefined}
            aria-describedby={fieldError ? errorId : undefined}
            onBlur={() => setSuggesting(false)}
            onCompositionStart={ime.onCompositionStart}
            onCompositionEnd={ime.onCompositionEnd}
            onKeyDown={(event) => {
              if (event.key === "Enter" && ime.isComposing(event)) {
                event.preventDefault() // no form submission
                event.preventBaseUIHandler() // and no suggestion accepted
              }
            }}
            className={cn(
              "min-w-0 pr-0 pl-1.5 text-base text-ellipsis placeholder:text-ellipsis",
              compact ? "h-10 pointer-coarse:h-11" : "h-12 pointer-coarse:h-12"
            )}
          />
          {/* Separate keys: one reused <button> would turn into type="submit" while its Stop click is still being
              handled, and the browser would then submit the form, starting a new search. */}
          {pending ? (
            <Button
              key="stop"
              {...action}
              data-composer-action=""
              type="button"
              onClick={(event) => {
                event.preventDefault()
                // The second click of a double-click on Search lands here: it must not stop the search it started.
                if (event.detail > 1) return
                cancel()
              }}
              className={buttonClass(compact, false)}
            >
              <SquareIcon aria-hidden className="size-3.5 fill-current" />
              Stop
            </Button>
          ) : (
            <Button
              key="search"
              {...action}
              data-composer-action=""
              type="submit"
              className={buttonClass(compact, true)}
            >
              <SearchIcon aria-hidden />
              <span className="max-sm:sr-only">Search</span>
            </Button>
          )}
        </AutocompleteInputGroup>
        {/* While the current text's suggestions load, the previous list stays, dimmed and marked busy. Always below
            the field, shrinking to the room left and scrolling: flipped above, a short landscape screen would put it
            over the heading and the header. */}
        <AutocompleteContent aria-busy={suggestions.stale || undefined} collisionAvoidance={{ side: "none" }}>
          <AutocompleteList>
            {(item: SuggestionItem) => <SuggestionOption key={item.work_id} item={item} />}
          </AutocompleteList>
        </AutocompleteContent>
      </Autocomplete>
      <div aria-live="polite">
        {fieldError && (
          <p id={errorId} className="mt-2 flex items-start gap-2 px-3 text-sm text-danger">
            <CircleAlertIcon aria-hidden className="mt-0.5 size-4 shrink-0" />
            <span className="min-w-0 wrap-break-word">{fieldError}</span>
          </p>
        )}
        {pausedUntil !== null && <PausedNote key={pausedUntil} until={pausedUntil} />}
      </div>
    </form>
  )
}

/**
 * One focus indicator: a 2px edge (1px border + 1px ring, so nothing shifts), teal, or danger while the text is
 * invalid; no soft halos. Unfocused and invalid: a 1px danger border (the message below carries icon and words).
 */
const COMPOSER_EDGE = [
  "has-[[data-slot=input-group-control]:focus-visible]:border-primary",
  "has-[[data-slot=input-group-control]:focus-visible]:ring-1",
  "has-[[data-slot=input-group-control]:focus-visible]:ring-primary",
  "has-[[data-slot][aria-invalid=true]]:ring-0",
  "has-[[data-slot=input-group-control][aria-invalid=true]:focus-visible]:border-destructive",
  "has-[[data-slot=input-group-control][aria-invalid=true]:focus-visible]:ring-1",
  "has-[[data-slot=input-group-control][aria-invalid=true]:focus-visible]:ring-destructive",
].join(" ")

/**
 * Search is icon-only below sm, so the placeholder gets the room (the hidden word keeps the accessible name). Stop
 * keeps its word at every width: a lone square is easy to misread while a search runs.
 */
function buttonClass(compact: boolean, iconOnlyOnPhones: boolean): string {
  return cn(
    "shrink-0",
    compact
      ? "h-10 rounded-[calc(var(--radius-composer)-0.375rem)] px-4 pointer-coarse:h-11"
      : "h-12 rounded-[calc(var(--radius-composer)-0.5rem)] px-5 text-base pointer-coarse:h-12",
    iconOnlyOnPhones
      ? compact
        ? "max-sm:w-10 max-sm:px-0 pointer-coarse:max-sm:w-11"
        : "max-sm:w-12 max-sm:px-0"
      : "max-sm:px-3.5"
  )
}

/**
 * The API asked to wait (Retry-After) and a search was refused meanwhile. Neutral, not an input error. The visible
 * number counts down; assistive tech hears the sentence once, when it first appears, not every second.
 */
function PausedNote({ until }: { until: number }) {
  const [start] = useState(() => Date.now())
  const [now, setNow] = useState(start)
  useEffect(() => {
    if (now >= until) return
    // Wake on the next whole second left, so the number changes exactly when it should and stops at 0.
    const timer = setTimeout(() => setNow(Date.now()), (until - now) % 1000 || 1000)
    return () => clearTimeout(timer)
  }, [until, now])
  const left = Math.ceil((until - now) / 1000)
  if (left <= 0) return null
  const total = Math.ceil((until - start) / 1000)
  return (
    <p className="mt-2 flex items-start gap-2 px-3 text-sm text-ink-secondary">
      <InfoIcon aria-hidden className="mt-0.5 size-4 shrink-0" />
      <span aria-hidden="true" className="tabular-nums">
        Searching resumes in {left} s.
      </span>
      <span className="sr-only">
        Searching resumes in {total} {total === 1 ? "second" : "seconds"}.
      </span>
    </p>
  )
}

/**
 * Search and Stop are separate elements, so the one with focus is removed when a search starts or ends. Hand focus to
 * its replacement, or a keyboard user would be dropped back to the top of the page.
 */
function useActionFocusHandoff(pending: boolean) {
  const ref = useRef<HTMLButtonElement | null>(null)
  // Removal fires no blur React can see, so this stays true when the focused button is swapped out.
  const hadFocus = useRef(false)
  useLayoutEffect(() => {
    const lost = document.activeElement === null || document.activeElement === document.body
    if (hadFocus.current && lost) ref.current?.focus()
  }, [pending])
  return {
    ref,
    onFocus: () => {
      hadFocus.current = true
    },
    onBlur: () => {
      hadFocus.current = false
    },
  }
}

/**
 * Home view on a mouse/trackpad device: start in the field. Never on touch (it would pop the keyboard over the page),
 * never for a restored ?entry= link, and never when something else already has focus (e.g. route focus).
 */
function useInitialFocus(inputRef: RefObject<HTMLInputElement | null>, home: boolean) {
  const { entry } = useSearch({ strict: false })
  const [shouldFocus] = useState(() => home && entry === undefined && matchesMedia("(pointer: fine)"))
  useEffect(() => {
    if (!shouldFocus) return
    const active = document.activeElement
    if (active && active !== document.body) return
    inputRef.current?.focus({ preventScroll: true })
  }, [shouldFocus, inputRef])
}

function matchesMedia(query: string): boolean {
  return typeof window.matchMedia === "function" && window.matchMedia(query).matches
}
