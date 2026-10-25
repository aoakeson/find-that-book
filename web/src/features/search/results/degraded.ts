import type { DegradedReason } from "@/api/types"

export interface DegradedNoticeText {
  /** warning: some books may be missing. info: the answer is complete but simpler (AI fallback). */
  tone: "info" | "warning"
  sentences: string[]
}

type AiCause = "not_configured" | "usage_limit" | "unavailable"
/** What fell back: reading the clues (typed), the explanations (standard), both, or neither named. */
type AiEffect = "both" | "typed" | "standard" | "unnamed"

const AI_SENTENCES: Record<AiCause, Record<AiEffect, string>> = {
  not_configured: {
    both: "AI isn't set up for this app, so this search used the words as typed and standard explanations.",
    typed: "AI isn't set up for this app, so this search used the words as typed.",
    standard: "AI isn't set up for this app, so the explanations use standard wording.",
    unnamed: "AI isn't set up for this app, so this search ran without it.",
  },
  // The daily budget and the short cooldown after a provider 429 both report ai_budget_exhausted.
  usage_limit: {
    both: "The AI usage limit was reached, so this search used the words as typed and standard explanations.",
    typed: "The AI usage limit was reached, so this search used the words as typed.",
    standard: "The AI usage limit was reached, so the explanations use standard wording.",
    unnamed: "The AI usage limit was reached, so parts of this search ran without AI help.",
  },
  unavailable: {
    both: "AI help wasn't available for this search, so it used the words as typed and standard explanations.",
    typed: "AI help wasn't available for this search, so it used the words as typed.",
    standard: "AI help wasn't available for this search, so the explanations use standard wording.",
    unnamed: "AI help wasn't available for part of this search.",
  },
}

/** Plain words for `degraded_reasons`. Degraded answers are valid answers: this explains, it never alarms. */
export function describeDegraded(reasons: readonly DegradedReason[]): DegradedNoticeText | null {
  if (reasons.length === 0) return null
  const has = (r: DegradedReason) => reasons.includes(r)
  const sentences: string[] = []

  // Without an AI provider nothing can read the clues, whether or not interpretation_fallback is listed too.
  const typed = has("interpretation_fallback") || has("ai_not_configured")
  const standard = has("explanation_fallback")
  const cause: AiCause | null = has("ai_not_configured")
    ? "not_configured"
    : has("ai_budget_exhausted")
      ? "usage_limit"
      : typed || standard
        ? "unavailable"
        : null
  if (cause) {
    const effect: AiEffect = typed && standard ? "both" : typed ? "typed" : standard ? "standard" : "unnamed"
    sentences.push(AI_SENTENCES[cause][effect])
  }
  if (has("catalog_partial")) sentences.push("Open Library didn't answer every search, so some books may be missing.")
  if (has("deadline_reached")) sentences.push("The search reached its time limit, so it shows what it found in time.")

  if (sentences.length === 0)
    sentences.push("Part of this search used a simpler fallback, so the results may be less precise.")
  return { tone: has("catalog_partial") || has("deadline_reached") ? "warning" : "info", sentences }
}
