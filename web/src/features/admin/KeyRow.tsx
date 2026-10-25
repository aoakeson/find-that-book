import { useEffect, useRef, useState } from "react"
import { useMutation } from "@tanstack/react-query"
import { Loader2Icon } from "lucide-react"
import { toast } from "sonner"
import type { ApiError } from "@/api/client"
import { clearAdminKey, probeProvider } from "@/api/endpoints"
import type { AdminAiStatus, AdminProbeResult, KeyName } from "@/api/types"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { formatCount, formatDuration } from "@/lib/format"
import { ADMIN_AI_QUERY_KEY, ADMIN_CHANGE_SCOPE, adminErrorMessage, useInFlight, useStoreStatus } from "./admin-api"
import { KeyForm } from "./KeyForm"
import { isChatProvider, keyLabels, keySourceWords, probeStatusWords } from "./labels"
import { InlineError, StateLine } from "./notes"

export interface KeyState {
  configured: boolean
  key_source: string
  key_fingerprint: string | null
}

interface KeyRowProps {
  name: KeyName
  keyState: KeyState
  model: string
  /** The provider searches use right now. */
  active?: boolean
  /**
   * The judge shows the same key as OpenAI (source and fingerprint). The API can't say whether that is its own copy or
   * the OpenAI key it falls back to, so the wording stays neutral and a key set here can always be removed.
   */
  sameAsOpenAi?: boolean
}

/** One key: its status in words, and add/replace, remove (runtime keys only) and test connection (chat providers). */
export function KeyRow({ name, keyState, model, active = false, sameAsOpenAi = false }: KeyRowProps) {
  const labels = keyLabels(name)
  const storeStatus = useStoreStatus()
  const [editing, setEditing] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const keyButtonRef = useRef<HTMLButtonElement>(null)
  const refocusKeyButton = useRef(false)
  const removeFlight = useInFlight()
  const probeFlight = useInFlight()

  const remove = useMutation<AdminAiStatus, ApiError>({
    mutationKey: [...ADMIN_AI_QUERY_KEY, "key", name, "remove"],
    scope: ADMIN_CHANGE_SCOPE,
    mutationFn: () => clearAdminKey(name),
    onSuccess: storeStatus,
  })

  const probe = useMutation<AdminProbeResult, ApiError>({
    mutationKey: [...ADMIN_AI_QUERY_KEY, "probe", name],
    mutationFn: () => {
      if (!isChatProvider(name)) throw new Error(`${name} can't be tested`)
      return probeProvider(name)
    },
  })

  // The key button is hidden while the form is open; give it focus back when the form closes.
  useEffect(() => {
    if (!editing && refocusKeyButton.current) {
      refocusKeyButton.current = false
      keyButtonRef.current?.focus()
    }
  }, [editing])

  function closeForm() {
    refocusKeyButton.current = true
    setEditing(false)
  }

  function keySaved() {
    probeFlight.end()
    probe.reset() // a result for the previous key would be misleading
    closeForm()
  }

  function confirmRemove() {
    setConfirmOpen(false)
    if (!removeFlight.start()) return
    remove.mutate(undefined, {
      onSuccess: () => {
        probeFlight.end()
        probe.reset()
        toast.success(`${labels.short} key removed`)
      },
      onSettled: () => {
        removeFlight.end()
        // The Remove button was busy (and is gone after a success), so focus may have dropped to the page: keep it in
        // this row. After the re-render, so the button's new label is what gets announced.
        requestAnimationFrame(() => {
          if (document.activeElement === null || document.activeElement === document.body) keyButtonRef.current?.focus()
        })
      },
    })
  }

  function testConnection() {
    if (!probeFlight.start()) return
    probe.mutate(undefined, { onSettled: probeFlight.end })
  }

  const { configured, key_source: source, key_fingerprint: fingerprint } = keyState
  const canRemove = source === "runtime" && !sameAsOpenAi
  const canTest = isChatProvider(name) && configured
  const hasOwnKey = configured && !sameAsOpenAi

  return (
    <li className="flex min-w-0 flex-col py-4 first:pt-0 last:pb-0">
      <div className="flex min-w-0 flex-col gap-3 sm:flex-row sm:items-start sm:justify-between sm:gap-6">
        <div className="flex min-w-0 flex-col gap-1">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <h4 className="font-medium wrap-break-word">{labels.title}</h4>
            {active && <Badge variant="secondary">In use</Badge>}
          </div>
          {model && <p className="text-caption wrap-break-word text-ink-secondary">Model {model}</p>}
          <StateLine tone={configured ? "ok" : "off"} className="text-sm">
            {sameAsOpenAi ? "Same key as OpenAI" : keySourceWords(configured, source)}
          </StateLine>
          {fingerprint && <p className="text-caption break-all text-ink-secondary">Fingerprint {fingerprint}</p>}
        </div>

        {!editing && (
          <div className="flex flex-wrap gap-2 sm:shrink-0 sm:justify-end">
            <Button
              ref={keyButtonRef}
              type="button"
              variant="outline"
              onClick={() => setEditing(true)}
              aria-label={`${hasOwnKey ? "Replace key" : "Add key"} for ${labels.short}`}
            >
              {hasOwnKey ? "Replace key" : "Add key"}
            </Button>
            {canTest && (
              <Button
                type="button"
                variant="outline"
                onClick={testConnection}
                disabled={probe.isPending}
                focusableWhenDisabled
                aria-label={`Test connection to ${labels.short}`}
              >
                {probe.isPending && <Loader2Icon aria-hidden="true" className="animate-spin" />}
                {probe.isPending ? "Testing…" : "Test connection"}
              </Button>
            )}
            {canRemove && (
              <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
                <AlertDialogTrigger
                  render={<Button type="button" variant="outline" className="text-danger hover:text-danger" />}
                  disabled={remove.isPending}
                  aria-label={`Remove the ${labels.short} key set here`}
                >
                  {remove.isPending && <Loader2Icon aria-hidden="true" className="animate-spin" />}
                  {remove.isPending ? "Removing…" : "Remove"}
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Remove the {labels.short} key set here?</AlertDialogTitle>
                    <AlertDialogDescription>
                      The key from configuration applies again, if there is one.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Cancel</AlertDialogCancel>
                    <AlertDialogAction variant="destructive" onClick={confirmRemove}>
                      Remove key
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            )}
          </div>
        )}
      </div>

      {editing && (
        <div className="mt-3">
          <KeyForm name={name} onSaved={keySaved} onCancel={closeForm} />
        </div>
      )}
      {remove.isError && (
        <div className="mt-3">
          <InlineError>
            {adminErrorMessage(remove.error, { fallback: "The API couldn't remove the key. Try again." })}
          </InlineError>
        </div>
      )}
      {isChatProvider(name) && (
        // Always mounted, so the outcome of a test is announced when it arrives.
        <div role="status">
          {probe.isPending ? (
            <p className="mt-3 text-sm text-ink-secondary">Testing the connection to {labels.short}…</p>
          ) : probe.data ? (
            <ProbeOutcome result={probe.data} />
          ) : probe.isError ? (
            <div className="mt-3">
              <InlineError>
                {adminErrorMessage(probe.error, { fallback: "The test couldn't run. Try again." })}
              </InlineError>
            </div>
          ) : null}
        </div>
      )}
    </li>
  )
}

function ProbeOutcome({ result }: { result: AdminProbeResult }) {
  if (result.ok) {
    const tokens = (result.input_tokens ?? 0) + (result.output_tokens ?? 0)
    const known = result.input_tokens !== null || result.output_tokens !== null
    return (
      <StateLine tone="ok" className="mt-3 text-sm">
        <p className="font-medium">Connected in {formatDuration(result.elapsed_ms)}</p>
        <p className="text-caption text-ink-secondary">
          {result.model}
          {known && `, ${formatCount(tokens)} tokens`}
        </p>
      </StateLine>
    )
  }
  return (
    <StateLine tone="error" className="mt-3 text-sm">
      <p className="font-medium text-danger">{probeStatusWords(result.status)}</p>
      <p className="text-caption wrap-break-word text-ink-secondary">{result.model}</p>
      {/* The API's own summary (keys already redacted server-side), one click away rather than on show. */}
      {result.detail && (
        <details className="text-caption text-ink-secondary">
          <summary className="cursor-pointer py-1 underline-offset-4 hover:underline">Details from the API</summary>
          <p className="wrap-break-word">{result.detail.slice(0, 300)}</p>
        </details>
      )}
    </StateLine>
  )
}
