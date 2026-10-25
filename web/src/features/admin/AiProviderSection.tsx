import { ExternalLinkIcon, LockKeyholeIcon, Loader2Icon, TriangleAlertIcon } from "lucide-react"
import type { AdminAiStatus } from "@/api/types"
import { ApiError } from "@/api/client"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from "@/components/ui/empty"
import { Skeleton } from "@/components/ui/skeleton"
import { adminErrorMessage, isUnavailable, useAdminAiStatus } from "./admin-api"
import { AdminPanel, AdminSection } from "./AdminSection"
import { KeyForm } from "./KeyForm"
import { KeyRow } from "./KeyRow"
import { isChatProvider, providerName } from "./labels"
import { StateNote } from "./notes"
import { ProviderSettings } from "./ProviderSettings"
import { SecondOpinionSetting } from "./SecondOpinionSetting"

export function AiProviderSection() {
  const query = useAdminAiStatus()
  const unavailable = query.isError && isUnavailable(query.error)
  return (
    <AdminSection
      title="AI provider"
      description={
        unavailable
          ? undefined
          : "Choose the AI that interprets searches and writes the explanations, and manage the keys it uses."
      }
    >
      <AiProviderBody />
    </AdminSection>
  )
}

function AiProviderBody() {
  const query = useAdminAiStatus()
  if (query.isError && isUnavailable(query.error)) return <AdminUnavailable />
  if (query.data) return <AiSettings status={query.data} />
  if (query.isError)
    return <LoadFailed error={query.error} retrying={query.isFetching} onRetry={() => void query.refetch()} />
  return <LoadingSettings />
}

function AiSettings({ status }: { status: AdminAiStatus }) {
  const openai = status.providers.find((p) => p.name === "openai")
  const judge = status.judge
  const judgeUsesOpenAiKey =
    judge.key_fingerprint !== null &&
    openai !== undefined &&
    judge.key_fingerprint === openai.key_fingerprint &&
    judge.key_source === openai.key_source

  return (
    <>
      <PersistenceNote persistence={status.persistence} />
      {!status.ai_configured && (status.provider_selection === "auto" || status.active_provider !== "none") && (
        <TurnOnAi status={status} />
      )}
      <AdminPanel title="Active provider">
        <ProviderSettings status={status} />
      </AdminPanel>
      <AdminPanel title="Second opinion">
        <SecondOpinionSetting judge={judge} />
      </AdminPanel>
      <AdminPanel title="Keys">
        <p className="-mt-2 max-w-measure text-sm text-ink-secondary">
          Keys can be replaced but never read back. Test connection makes one small real call to the provider and uses a
          few tokens.
        </p>
        <ul className="flex flex-col divide-y divide-line">
          {status.providers.map(
            (p) =>
              isChatProvider(p.name) && (
                <KeyRow key={p.name} name={p.name} keyState={p} model={p.model} active={p.active} />
              )
          )}
          <KeyRow name="judge" keyState={judge} model={judge.model} sameAsOpenAi={judgeUsesOpenAiKey} />
        </ul>
      </AdminPanel>
    </>
  )
}

const KEY_PAGES: Record<string, string> = {
  gemini: "https://aistudio.google.com/apikey",
  anthropic: "https://console.anthropic.com/settings/keys",
  openai: "https://platform.openai.com/api-keys",
}

/**
 * First run, no usable key: both ways to turn AI on, side by side. Pasting here works at once (until the API
 * restarts); .env keeps it. Searching already works without either.
 */
function TurnOnAi({ status }: { status: AdminAiStatus }) {
  const target = isChatProvider(status.active_provider) ? status.active_provider : "gemini"
  const name = providerName(target)
  return (
    <AdminPanel title="Turn on AI">
      <p className="-mt-2 max-w-measure text-ink-secondary">
        Searches already work without AI, using basic matching. Add a {name} key to let AI read the clues and write the
        explanations. Use whichever way you prefer:
      </p>
      <div className="grid gap-4 md:grid-cols-2">
        <div className="flex min-w-0 flex-col gap-2">
          <h4 className="font-medium">Paste it here</h4>
          <p className="text-sm text-ink-secondary">Works straight away, until the API restarts.</p>
          <KeyForm key={target} name={target} onSaved={() => {}} />
        </div>
        <div className="flex min-w-0 flex-col gap-2">
          <h4 className="font-medium">Or put it in .env</h4>
          <p className="text-sm text-ink-secondary">Kept across restarts. In the repo root:</p>
          <ol className="flex list-decimal flex-col gap-1.5 pl-5 text-sm">
            <li>
              Copy <code className="font-medium">.env.example</code> to <code className="font-medium">.env</code>.
            </li>
            <li>
              Set{" "}
              <code className="font-medium break-all">
                {target === "gemini"
                  ? "GEMINI_API_KEY"
                  : target === "anthropic"
                    ? "ANTHROPIC_API_KEY"
                    : "OPENAI_API_KEY"}
                =your-key
              </code>
              .
            </li>
            <li>Restart the app.</li>
          </ol>
        </div>
      </div>
      <a
        href={KEY_PAGES[target]}
        target="_blank"
        rel="noopener noreferrer"
        className="inline-flex w-fit items-center gap-1.5 py-2 text-sm font-medium text-teal underline-offset-4 hover:text-teal-hover hover:underline pointer-coarse:min-h-11"
      >
        {target === "gemini" ? "Get a free Gemini key" : `Get a ${name} key`}
        <ExternalLinkIcon aria-hidden="true" className="size-3.5" />
        <span className="sr-only"> (opens in a new tab)</span>
      </a>
    </AdminPanel>
  )
}

/** Worded from the API's `persistence` field, so the page never promises more than the API said. */
function PersistenceNote({ persistence }: { persistence: string }) {
  const inMemory = /^\s*memory\b/i.test(persistence)
  return (
    <StateNote tone="info">
      {inMemory
        ? "Changes here last until the API restarts. Your .env file is never changed."
        : `Changes here are kept in: ${persistence}.`}
    </StateNote>
  )
}

function AdminUnavailable() {
  return (
    <Empty className="rounded-panel border border-dashed border-line bg-surface">
      <EmptyHeader>
        <EmptyMedia
          variant="icon"
          className="size-10 rounded-lg bg-sage text-teal [&_svg:not([class*='size-'])]:size-5"
        >
          <LockKeyholeIcon aria-hidden="true" />
        </EmptyMedia>
        <EmptyTitle className="text-base font-semibold">Admin isn't available here</EmptyTitle>
        <EmptyDescription className="text-ink-secondary">
          These settings only work while the API runs in Development mode, and only from the API's own machine.
          Searching works as usual.
        </EmptyDescription>
      </EmptyHeader>
    </Empty>
  )
}

function LoadFailed({ error, retrying, onRetry }: { error: Error; retrying: boolean; onRetry: () => void }) {
  const reference = error instanceof ApiError ? error.traceId : null
  return (
    <Alert className="border-danger-line bg-danger-surface">
      <TriangleAlertIcon aria-hidden="true" className="text-danger" />
      <AlertTitle className="text-danger">Couldn't load the AI settings</AlertTitle>
      <AlertDescription className="flex flex-col items-start gap-0 text-pretty text-ink [&_p:not(:last-child)]:mb-3">
        <p>{adminErrorMessage(error, { fallback: "The API couldn't load its settings. Try again." })}</p>
        {reference && <p className="text-caption break-all text-ink-secondary">Reference {reference}</p>}
        <Button type="button" variant="outline" onClick={onRetry} disabled={retrying} focusableWhenDisabled>
          {retrying && <Loader2Icon aria-hidden="true" className="animate-spin" />}
          Try again
        </Button>
      </AlertDescription>
    </Alert>
  )
}

/** A still outline of the settings (no pulse: motion is reserved for the search). */
function LoadingSettings() {
  return (
    <div role="status" className="flex flex-col gap-4">
      <span className="sr-only">Loading the AI settings…</span>
      <Skeleton className="h-11 animate-none rounded-lg" />
      {[0, 1].map((i) => (
        <div
          key={i}
          aria-hidden="true"
          className="flex flex-col gap-3 rounded-panel border border-line bg-surface p-4 sm:p-6"
        >
          <Skeleton className="h-5 w-32 animate-none" />
          <Skeleton className="h-4 w-full max-w-md animate-none" />
          <Skeleton className="h-10 w-full max-w-xs animate-none" />
        </div>
      ))}
    </div>
  )
}
