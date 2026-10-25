import { useId, useState } from "react"
import { Loader2Icon } from "lucide-react"
import { toast } from "sonner"
import type { AdminAiStatus, ProviderChoice } from "@/api/types"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { adminErrorMessage, useInFlight, useUpdateAdminAi } from "./admin-api"
import { PROVIDER_CHOICES, providerName } from "./labels"
import { InlineError, StateNote, type Tone } from "./notes"

/** Which provider is active, in words, and the control to switch it. */
export function ProviderSettings({ status }: { status: AdminAiStatus }) {
  const update = useUpdateAdminAi()
  const flight = useInFlight()
  // null = follow the API's value; set once the user picks something else.
  const [choice, setChoice] = useState<ProviderChoice | null>(null)
  const labelId = useId()
  const hintId = useId()
  const errorId = useId()

  const current: ProviderChoice =
    status.provider_selection === "auto" ? "auto" : (status.active_provider as ProviderChoice)
  const selected = choice ?? current
  const changed = selected !== current
  const chosen = status.providers.find((p) => p.name === selected)
  const keyMissing = changed && chosen !== undefined && !chosen.configured

  function save() {
    if (!changed || !flight.start()) return
    update.mutate(
      { provider: selected as ProviderChoice },
      {
        onSuccess: (next) => {
          setChoice(null)
          toast.success(
            next.provider_selection === "auto"
              ? "Provider set to automatic"
              : next.active_provider === "none"
                ? "AI turned off"
                : `Provider set to ${providerName(next.active_provider)}`
          )
        },
        onSettled: flight.end,
      }
    )
  }

  const summary = describeActive(status)
  const describedBy =
    [keyMissing ? hintId : null, update.isError ? errorId : null].filter(Boolean).join(" ") || undefined

  return (
    <div className="flex flex-col gap-4">
      <StateNote tone={summary.tone}>{summary.text}</StateNote>
      <div className="flex flex-col gap-2">
        <Label id={labelId}>Use for searches</Label>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
          <Select
            items={PROVIDER_CHOICES}
            value={selected}
            onValueChange={(value) => {
              if (typeof value === "string") setChoice(value as ProviderChoice)
            }}
            disabled={update.isPending}
          >
            <SelectTrigger
              aria-labelledby={labelId}
              aria-describedby={describedBy}
              className="w-full bg-surface text-base sm:w-60"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {PROVIDER_CHOICES.map((option) => (
                <SelectItem
                  key={option.value}
                  value={option.value}
                  className="min-h-10 text-base pointer-coarse:min-h-11"
                >
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button
            type="button"
            onClick={save}
            disabled={!changed || update.isPending}
            focusableWhenDisabled
            className="self-start sm:self-auto"
          >
            {update.isPending && <Loader2Icon aria-hidden="true" className="animate-spin" />}
            {update.isPending ? "Saving…" : "Save provider"}
          </Button>
        </div>
        {keyMissing && chosen && (
          <p id={hintId} className="text-sm text-ink-secondary">
            {providerName(chosen.name)} has no key yet. Add one below, or searches run without AI.
          </p>
        )}
        {update.isError && (
          <InlineError id={errorId}>{adminErrorMessage(update.error, { field: "Provider" })}</InlineError>
        )}
      </div>
    </div>
  )
}

function describeActive(status: AdminAiStatus): { tone: Tone; text: string } {
  const name = status.active_provider
  const auto = status.provider_selection === "auto"
  if (name === "none") {
    return auto
      ? { tone: "warning", text: "Automatic: no provider has a key yet, so searches run without AI. Add a key below." }
      : { tone: "off", text: "AI is off. Searches still work, without AI." }
  }
  const label = providerName(name)
  const active = status.providers.find((p) => p.name === name)
  if (status.ai_configured) {
    const using = active ? `${label}, using ${active.model}` : label
    return { tone: "ok", text: auto ? `AI is on: ${using} (picked automatically).` : `AI is on: ${using}.` }
  }
  if (active && !active.configured) {
    return {
      tone: "warning",
      text: `${label} is selected but has no key, so searches run without AI. Add a key for it below.`,
    }
  }
  return {
    tone: "warning",
    text: `${label} is selected but isn't available right now, so searches run without AI. Test the connection below to see why.`,
  }
}
