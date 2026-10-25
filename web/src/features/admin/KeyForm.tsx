import { useEffect, useId, useRef, useState, type FormEvent } from "react"
import { useMutation } from "@tanstack/react-query"
import { Loader2Icon } from "lucide-react"
import { toast } from "sonner"
import { ApiError } from "@/api/client"
import { setAdminKey, updateAdminAi } from "@/api/endpoints"
import type { AdminAiStatus, ChatProvider, KeyName } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Field, FieldDescription, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { ADMIN_AI_QUERY_KEY, ADMIN_CHANGE_SCOPE, adminErrorMessage, useInFlight, useStoreStatus } from "./admin-api"
import { isChatProvider, keyLabels } from "./labels"
import { InlineError } from "./notes"

/**
 * Entering a new key. The key exists only in this password input's DOM value:
 * - it is not React state, a mutation variable, a query, a URL, storage or a log line;
 * - the save mutation takes no variables and reads the input when the request starts, so TanStack's mutation cache
 *   (variables, options, state) never sees it; it leaves the page in that one request body;
 * - the input has no `name`, so even a native form submission could not put it in a URL;
 * - it is cleared on success, on Cancel (the form unmounts) and on unmount. After a failed save it stays, so the
 *   user can fix it.
 */
export function KeyForm({
  name,
  onSaved,
  onCancel,
}: {
  name: KeyName
  onSaved: () => void
  /** Omit when the form is always shown (the "Turn on AI" panel): there is nothing to cancel back to. */
  onCancel?: () => void
}) {
  // Opened by "Add key", it takes focus; always on screen (no onCancel), it waits for the user.
  const autoFocus = onCancel !== undefined
  const labels = keyLabels(name)
  const inputRef = useRef<HTMLInputElement>(null)
  const flight = useInFlight()
  const storeStatus = useStoreStatus()
  const [emptyError, setEmptyError] = useState(false)
  const inputId = useId()
  const descriptionId = useId()
  const errorId = useId()

  const save = useMutation<{ status: AdminAiStatus; switched: boolean }, ApiError>({
    mutationKey: [...ADMIN_AI_QUERY_KEY, "key", name],
    scope: ADMIN_CHANGE_SCOPE,
    mutationFn: async () => {
      const key = readKey(inputRef.current)
      // Queued behind another admin change and the form closed meanwhile: there is no key any more, so send nothing.
      if (key === "") throw new ApiError("aborted", "The key form was closed before the save started.")
      const status = await setAdminKey(name, key)
      if (!shouldSwitchTo(name, status)) return { status, switched: false }
      // The selected provider has no key and this is the first one: use it, so adding a key is all it takes.
      try {
        return { status: await updateAdminAi({ provider: name as ChatProvider }), switched: true }
      } catch {
        return { status, switched: false }
      }
    },
    onSuccess: ({ status, switched }) => {
      clearKey(inputRef.current)
      void storeStatus(status)
      toast.success(
        switched ? `${labels.short} key saved. Searches now use ${labels.short}.` : `${labels.short} key saved`
      )
    },
  })

  useEffect(() => {
    const input = inputRef.current
    if (autoFocus) input?.focus()
    return () => clearKey(input)
  }, [autoFocus])

  function submit(event: FormEvent) {
    event.preventDefault()
    if (readKey(inputRef.current) === "") {
      setEmptyError(true)
      inputRef.current?.focus()
      return
    }
    if (!flight.start()) return
    setEmptyError(false)
    save.mutate(undefined, {
      onSuccess: onSaved,
      onError: () => inputRef.current?.focus(),
      onSettled: flight.end,
    })
  }

  function cancel() {
    clearKey(inputRef.current)
    onCancel?.()
  }

  function edited() {
    if (emptyError) setEmptyError(false)
    if (save.isError) save.reset()
  }

  const fieldError = emptyError ? "Paste the key first." : (save.error?.fieldErrors("ApiKey")[0] ?? null)
  const otherError =
    !fieldError && save.isError
      ? adminErrorMessage(save.error, { fallback: "The API couldn't save the key. Try again." })
      : null
  const description =
    name === "judge"
      ? "An OpenAI key for the second opinion only. Without one, it uses the OpenAI key. It's never shown again."
      : "It goes to the API on this machine and is never shown again; you'll see only its fingerprint."

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-3 rounded-lg bg-mist p-3 sm:p-4">
      <Field data-invalid={fieldError ? true : undefined} className="gap-1.5">
        <FieldLabel htmlFor={inputId}>{labels.short} API key</FieldLabel>
        <Input
          ref={inputRef}
          id={inputId}
          type="password"
          autoComplete="off"
          autoCapitalize="off"
          autoCorrect="off"
          spellCheck={false}
          maxLength={512}
          readOnly={save.isPending}
          onChange={edited}
          aria-invalid={fieldError ? true : undefined}
          aria-describedby={fieldError ? `${errorId} ${descriptionId}` : descriptionId}
          data-1p-ignore=""
          data-lpignore="true"
        />
        <FieldDescription id={descriptionId}>{description}</FieldDescription>
        {fieldError && <InlineError id={errorId}>{fieldError}</InlineError>}
      </Field>
      {otherError && <InlineError>{otherError}</InlineError>}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={save.isPending} focusableWhenDisabled>
          {save.isPending && <Loader2Icon aria-hidden="true" className="animate-spin" />}
          {save.isPending ? "Saving…" : "Save key"}
        </Button>
        {onCancel && (
          <Button type="button" variant="outline" onClick={cancel} disabled={save.isPending}>
            Cancel
          </Button>
        )}
      </div>
    </form>
  )
}

/**
 * AI is off only because the explicitly chosen provider has no key (not because the user chose "none"). Automatic
 * selection needs no switch: the API picks up the new key by itself.
 */
function shouldSwitchTo(name: KeyName, status: AdminAiStatus): boolean {
  if (status.provider_selection === "auto") return false
  if (!isChatProvider(name) || status.ai_configured || status.active_provider === name) return false
  const active = status.providers.find((p) => p.name === status.active_provider)
  return active !== undefined && !active.configured
}

function readKey(input: HTMLInputElement | null): string {
  return input?.value.trim() ?? ""
}

function clearKey(input: HTMLInputElement | null) {
  if (input) input.value = ""
}
