import type { CatalogStep, JudgeStatus, LlmCallStatus, SearchResponse } from "@/api/types"
import { formatCount, formatDuration, formatList } from "@/lib/format"

/**
 * The "Search activity" steps, derived only from what the response reports (meta + interpretation). The API has no
 * progress events, so this exists only after a search finishes; a stage the response doesn't time never appears.
 */
export interface ActivityStep {
  id: "interpret" | "retrieve" | "rank" | "explain" | "judge"
  title: string
  durationMs: number | null
  /** A recovered problem (AI fell back, a later catalog call failed). Never a failed search. */
  fallback: boolean
  /** One plain sentence about how the step went, when there is something to say. */
  note: string | null
  details: ActivityDetail[]
}

export interface ActivityDetail {
  text: string
  description?: string
  warning?: boolean
}

export interface Activity {
  elapsedMs: number
  steps: ActivityStep[]
  /** Small print: AI model and token counts when known. */
  footnotes: string[]
  /** For support: the server's id for this request. */
  requestId: string
}

export function describeActivity(response: SearchResponse): Activity {
  const { meta } = response
  const timings = meta.timings_ms
  const steps: ActivityStep[] = []

  const interpretCall = meta.llm.steps.find((s) => s.operation === "interpret")
  if ("interpret" in timings || interpretCall) {
    steps.push(interpretStep(response, timings.interpret ?? interpretCall?.elapsed_ms ?? null, interpretCall?.status))
  }
  if ("retrieve" in timings || meta.open_library.steps.length > 0) {
    const requeries = meta.open_library.slow_empty_requeries ?? 0
    steps.push({
      id: "retrieve",
      title: "Searching Open Library",
      durationMs: timings.retrieve ?? null,
      fallback: false,
      note:
        requeries === 1
          ? "Asked again once after a slow empty answer."
          : requeries > 1
            ? `Asked again ${requeries} times after slow empty answers.`
            : null,
      details: meta.open_library.steps.map(catalogDetail),
    })
  }
  if ("rank" in timings) {
    steps.push({
      id: "rank",
      title: "Comparing matches",
      durationMs: timings.rank,
      fallback: false,
      note: null,
      details: [],
    })
  }
  const explainCall = meta.llm.steps.find((s) => s.operation === "explain")
  if ("explain" in timings || explainCall) {
    steps.push(explainStep(response, timings.explain ?? explainCall?.elapsed_ms ?? null, explainCall?.status))
  }
  if (meta.judge.status !== "off" && meta.judge.status !== "skipped") {
    steps.push(judgeStep(response, timings.judge ?? meta.judge.latency_ms ?? null))
  }

  return { elapsedMs: meta.elapsed_ms, steps, footnotes: footnotes(response), requestId: meta.request_id }
}

/**
 * A fallback's cause is stated only when the response states it: the LLM call's own status, or a degraded reason
 * that covers every AI call. Otherwise the step says what happened without guessing why (the backend also skips AI
 * work when the deadline is close, without recording a call).
 */
function statedCause(response: SearchResponse, status: LlmCallStatus | undefined): string | null {
  // An unknown status from a newer API still gets a true, cause-free sentence.
  if (status !== undefined && status !== "ok") return LLM_STATUS_CAUSES[status] ?? "The AI call didn't succeed"
  if (status === undefined && response.degraded_reasons.includes("ai_not_configured"))
    return LLM_STATUS_CAUSES.not_configured
  if (status === undefined && response.degraded_reasons.includes("ai_budget_exhausted"))
    return LLM_STATUS_CAUSES.budget_exhausted
  return null
}

function interpretStep(
  response: SearchResponse,
  durationMs: number | null,
  status: LlmCallStatus | undefined
): ActivityStep {
  const i = response.interpretation
  const details: ActivityDetail[] = []
  const usedAi = i.source === "llm"

  if (i.intent === "topic" && i.topics.length > 0) details.push({ text: `Books about: ${formatList(i.topics)}` })
  if (usedAi) {
    if (i.title) details.push({ text: `Title: ${i.title}` })
    if (i.original_title) details.push({ text: `Original title: ${i.original_title}` })
    if (i.english_title) details.push({ text: `English title: ${i.english_title}` })
    if (i.author) details.push({ text: `Author: ${i.author}` })
    if (i.characters.length > 0) details.push({ text: `Characters: ${i.characters.join(", ")}` })
    if (i.keywords.length > 0) details.push({ text: `Other clues: ${i.keywords.join(", ")}` })
  } else if (i.keywords.length > 0) {
    details.push({ text: `Searched for: ${i.keywords.join(", ")}` })
  }
  if (i.year !== null) details.push({ text: `Year: ${i.year}` })
  if (i.removed_noise.length > 0) details.push({ text: `Ignored: ${i.removed_noise.join(", ")}` })
  if (usedAi && details.length === 0) details.push({ text: "No title or author recognised" })

  const cause = usedAi ? null : statedCause(response, status)
  return {
    id: "interpret",
    title: "Understanding your clues",
    durationMs,
    fallback: !usedAi && (cause !== null || response.degraded_reasons.includes("interpretation_fallback")),
    note: usedAi ? null : cause ? `${cause}, so it used the words as typed.` : "Used the words as typed.",
    details,
  }
}

function explainStep(
  response: SearchResponse,
  durationMs: number | null,
  status: LlmCallStatus | undefined
): ActivityStep {
  const total = response.candidates.length
  const standard = response.candidates.filter((c) => c.explanation_source === "template").length
  const cause = statedCause(response, status)
  let note: string | null = null
  if (cause) note = `${cause}, so the explanations use standard wording.`
  else if (standard > 0 && standard === total) note = "Used standard wording."
  else if (standard > 0)
    note = `${standard} of ${total} explanations ${standard === 1 ? "uses" : "use"} standard wording.`
  return {
    id: "explain",
    title: "Writing explanations",
    durationMs,
    fallback: cause !== null || (standard > 0 && response.degraded_reasons.includes("explanation_fallback")),
    note,
    details: [],
  }
}

function judgeStep(response: SearchResponse, durationMs: number | null): ActivityStep {
  const { judge } = response.meta
  const pick = judge.pick ? response.candidates.find((c) => c.work_id === judge.pick) : undefined
  const notes: Record<Exclude<JudgeStatus, "off" | "skipped">, string> = {
    agreed: "A second AI check agreed with the first result.",
    disagreed: pick ? `A second AI check preferred “${pick.title}”.` : "A second AI check preferred a different book.",
    chose_none: "A second AI check thought none of these fit well.",
    refused: "The second AI check declined to answer, so the result stands as ranked.",
    unavailable: "The second AI check wasn't available, so the result stands as ranked.",
  }
  const status = judge.status as Exclude<JudgeStatus, "off" | "skipped">
  return {
    id: "judge",
    title: "Second opinion",
    durationMs,
    fallback: status === "refused" || status === "unavailable",
    note: notes[status] ?? null,
    details: [],
  }
}

const CATALOG_STEP_LABELS: Record<string, string> = {
  broad: "Searched with all your words",
  short_title: "Searched titles for your words",
  hypothesis: "Searched the likely title and author",
  original_title: "Searched the original title",
  person: "Searched for the characters you named",
  person_any: "Searched for any of the characters",
  any_of: "Tried a broader search",
  person_fallback: "Tried your words as character names",
  person_fallback_any: "Tried any of your words as character names",
  author_works: "Looked up the author's best-known works",
}

export function catalogStepLabel(step: string): string {
  return CATALOG_STEP_LABELS[step] ?? humanise(step)
}

function catalogDetail(step: CatalogStep): ActivityDetail {
  const parts: string[] = []
  const warning = step.outcome !== "ok"
  if (step.outcome === "failed") parts.push("Open Library didn't answer")
  else if (step.outcome === "rejected") parts.push("Open Library refused this search")
  else if (step.outcome === "deadline") parts.push("stopped at the time limit")
  else if (step.results !== null)
    parts.push(
      step.results === 0 ? "no results" : `${formatCount(step.results)} ${step.results === 1 ? "result" : "results"}`
    )

  if (step.cache_hit) parts.push("from cache")
  else parts.push(formatDuration(step.elapsed_ms))
  if (step.attempts === 2) parts.push("retried once")
  else if (step.attempts > 2) parts.push(`retried ${step.attempts - 1} times`)

  return { text: catalogStepLabel(step.step), description: capitalise(parts.join(", ")), warning }
}

const LLM_STATUS_CAUSES: Record<Exclude<LlmCallStatus, "ok">, string> = {
  not_configured: "AI isn't set up",
  rate_limited: "AI was busy",
  // Both the provider's daily budget and the short cooldown after a 429 report this.
  budget_exhausted: "The AI usage limit was reached",
  timeout: "AI took too long",
  provider_error: "AI was unavailable",
  malformed_output: "AI's answer couldn't be used",
  truncated: "AI's answer couldn't be used",
  refused: "AI declined to answer",
}

function footnotes(response: SearchResponse): string[] {
  const { llm, judge } = response.meta
  const notes: string[] = []
  if (llm.model) {
    const model =
      llm.provider && !llm.model.toLowerCase().includes(llm.provider.toLowerCase())
        ? `${llm.model} (${llm.provider})`
        : llm.model
    const tokens =
      llm.calls > 0 && llm.input_tokens !== null && llm.output_tokens !== null
        ? `, ${formatCount(llm.input_tokens)} tokens in, ${formatCount(llm.output_tokens)} out`
        : ""
    notes.push(`AI model: ${model}${tokens}`)
  }
  if (judge.model && judge.status !== "off" && judge.status !== "skipped")
    notes.push(`Second opinion model: ${judge.model}`)
  return notes
}

function humanise(name: string): string {
  return capitalise(name.replace(/[_-]+/g, " ").trim()) || "Search step"
}

function capitalise(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1)
}
