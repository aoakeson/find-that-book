import { useId } from "react"
import { toast } from "sonner"
import type { AdminJudgeStatus } from "@/api/types"
import { Switch } from "@/components/ui/switch"
import { adminErrorMessage, useInFlight, useUpdateAdminAi } from "./admin-api"
import { InlineError } from "./notes"

/** The judge on/off switch. It always shows the API's value; a change appears once the API confirms it. */
export function SecondOpinionSetting({ judge }: { judge: AdminJudgeStatus }) {
  const update = useUpdateAdminAi()
  const flight = useInFlight()
  const labelId = useId()
  const descriptionId = useId()
  const errorId = useId()

  function toggle(enabled: boolean) {
    if (!flight.start()) return
    update.mutate(
      { judge_enabled: enabled },
      {
        onSuccess: (next) =>
          toast.success(next.judge.enabled ? "Second opinion turned on" : "Second opinion turned off"),
        onSettled: flight.end,
      }
    )
  }

  return (
    <div className="flex flex-col gap-2">
      <label className="flex min-h-11 w-fit cursor-pointer items-center gap-3">
        <Switch
          checked={judge.enabled}
          onCheckedChange={toggle}
          readOnly={update.isPending}
          aria-labelledby={labelId}
          aria-describedby={[descriptionId, update.isError ? errorId : null].filter(Boolean).join(" ")}
        />
        <span id={labelId} className="font-medium">
          Use the second opinion
        </span>
        {update.isPending && <span className="text-sm text-ink-secondary">Saving…</span>}
      </label>
      <p id={descriptionId} className="max-w-measure text-sm text-ink-secondary">
        A second model, OpenAI Decisions ({judge.model}), checks the top result before it's called the best match. It
        never changes the order.
        {!judge.configured && " It has no key yet: add a second opinion key or an OpenAI key below."}
      </p>
      {update.isError && <InlineError id={errorId}>{adminErrorMessage(update.error)}</InlineError>}
    </div>
  )
}
