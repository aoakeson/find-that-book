/*
 * The Find That Book API contract (docs/API.md, /openapi/v1.json), mirrored by hand.
 * contract.test.ts checks every field name and enum value here against the API's OpenAPI document.
 * JSON is snake_case; every field is always present and unknown values are null.
 */

export const MATCH_STATUSES = ["match", "ambiguous", "none"] as const
export type MatchStatus = (typeof MATCH_STATUSES)[number]

export const MATCH_TIERS = ["a", "b", "c", "d", "e"] as const
export type MatchTier = (typeof MATCH_TIERS)[number]

export const STATUS_REASONS = [
  "exact_title_primary_author",
  "exact_title_contributor_author",
  "exact_title",
  "near_title_author",
  "author_only",
  "close_scores",
  "score_conflict",
  "unsupported_guess",
  "weak_evidence",
  "judge_agreed",
  "judge_low_confidence",
  "judge_disagreed",
  "judge_chose_none",
  "no_candidates",
  "no_searchable_terms",
  "topic_search",
] as const
export type StatusReason = (typeof STATUS_REASONS)[number]

export const DEGRADED_REASONS = [
  "ai_not_configured",
  "ai_budget_exhausted",
  "interpretation_fallback",
  "explanation_fallback",
  "catalog_partial",
  "deadline_reached",
] as const
export type DegradedReason = (typeof DEGRADED_REASONS)[number]

/** book: one particular book. topic: any books about a subject ("find me a book about bears"). */
export const QUERY_INTENTS = ["book", "topic"] as const
export type QueryIntent = (typeof QUERY_INTENTS)[number]

export const INTERPRETATION_SOURCES = ["llm", "heuristic"] as const
export type InterpretationSource = (typeof INTERPRETATION_SOURCES)[number]

export const EXPLANATION_SOURCES = ["llm", "template"] as const
export type ExplanationSource = (typeof EXPLANATION_SOURCES)[number]

export const JUDGE_STATUSES = ["off", "skipped", "agreed", "disagreed", "chose_none", "refused", "unavailable"] as const
export type JudgeStatus = (typeof JUDGE_STATUSES)[number]

export const CATALOG_CALL_OUTCOMES = ["ok", "failed", "rejected", "deadline"] as const
export type CatalogCallOutcome = (typeof CATALOG_CALL_OUTCOMES)[number]

export const LLM_CALL_STATUSES = [
  "ok",
  "not_configured",
  "budget_exhausted",
  "timeout",
  "rate_limited",
  "provider_error",
  "malformed_output",
  "truncated",
  "refused",
] as const
export type LlmCallStatus = (typeof LLM_CALL_STATUSES)[number]

export const SUGGEST_MATCHES = ["title", "word", "author"] as const
export type SuggestMatch = (typeof SUGGEST_MATCHES)[number]

/** GET /api/books/search → 200 for match, ambiguous and none alike. */
export interface SearchResponse {
  /** The query as searched: trimmed, control and zero-width characters removed. */
  query: string
  status: MatchStatus
  status_reason: StatusReason
  /** What to add to narrow the search; set for ambiguous and none. */
  hint: string | null
  degraded: boolean
  degraded_reasons: DegradedReason[]
  interpretation: SearchInterpretation
  candidates: SearchCandidate[]
  meta: SearchMeta
}

export interface SearchInterpretation {
  title: string | null
  author: string | null
  characters: string[]
  keywords: string[]
  english_title: string | null
  original_title: string | null
  year: number | null
  removed_noise: string[]
  source: InterpretationSource
  intent: QueryIntent
  /** The subject terms searched for a topic request; empty for a book request. */
  topics: string[]
}

export interface SearchCandidate {
  rank: number
  work_id: string
  title: string
  subtitle: string | null
  /** Primary authors joined with ", " (at most three, then "and N more"); null when Open Library lists none. */
  author: string | null
  primary_authors: AuthorLink[]
  primary_authors_total: number
  contributors: ContributorLink[]
  contributors_total: number
  first_publish_year: number | null
  edition_count: number
  open_library_url: string
  cover_url: string | null
  match_tier: MatchTier
  match_tier_label: string
  /** Topic requests: up to three of the work's Open Library subject headings that matched; empty otherwise. */
  matched_subjects: string[]
  explanation: string
  explanation_source: ExplanationSource
  /** True only on the judge's pick when it disagreed with rank 1. */
  second_opinion: boolean
  duplicates_folded: number
}

export interface AuthorLink {
  key: string | null
  name: string
  url: string | null
}

export interface ContributorLink {
  key: string | null
  name: string
  role: string | null
}

export interface SearchMeta {
  request_id: string
  elapsed_ms: number
  /** Per stage, only the stages that ran: clean, interpret, retrieve, rank, explain, judge. */
  timings_ms: Record<string, number>
  open_library: OpenLibraryMeta
  llm: LlmMeta
  judge: JudgeMeta
}

export interface OpenLibraryMeta {
  calls: number
  cache_hits: number
  upstream_requests: number
  retries: number
  /** Calls whose slow empty answer was asked once more (counted as attempts, not retries). */
  slow_empty_requeries: number
  budget: number
  steps: CatalogStep[]
}

export interface CatalogStep {
  /** broad, short_title, hypothesis, original_title, person, person_any, any_of, person_fallback, author_works… */
  step: string
  outcome: CatalogCallOutcome
  cache_hit: boolean
  attempts: number
  elapsed_ms: number
  results: number | null
  num_found: number | null
}

export interface LlmMeta {
  provider: string | null
  model: string | null
  calls: number
  cache_hits: number
  input_tokens: number | null
  output_tokens: number | null
  est_cost_usd: number | null
  prompt_versions: Record<string, string>
  steps: LlmStep[]
}

export interface LlmStep {
  /** interpret or explain. */
  operation: string
  provider: string
  model: string
  prompt_id: string
  status: LlmCallStatus
  cache_hit: boolean
  elapsed_ms: number
  input_tokens: number | null
  output_tokens: number | null
}

export interface JudgeMeta {
  enabled: boolean
  status: JudgeStatus
  pick: string | null
  probability: number | null
  threshold: number
  agreed: boolean | null
  model: string | null
  latency_ms: number | null
  input_tokens: number | null
  est_cost_usd: number | null
  detail: string | null
}

/** GET /api/suggest: a local index lookup, no upstream calls. */
export interface SuggestResponse {
  prefix: string
  suggestions: SuggestionItem[]
}

export interface SuggestionItem {
  work_id: string
  title: string
  author: string | null
  first_publish_year: number | null
  cover_url: string | null
  edition_count: number
  open_library_url: string
  match: SuggestMatch
}

/** GET /api/health: never calls Open Library or an LLM. */
export interface HealthResponse {
  status: string
  version: string
  environment: string
  ai_provider: string | null
  ai_configured: boolean | null
  judge_enabled: boolean | null
  suggest_index: SuggestIndexInfo | null
  upstream: UpstreamHealth | null
}

export interface SuggestIndexInfo {
  version: string
  count: number
}

export interface UpstreamHealth {
  open_library_circuit: string | null
  open_library_last_failure: string | null
  open_library_last_failure_reason: string | null
  llm_status: string | null
  llm_last_failure: string | null
  llm_last_failure_reason: string | null
}

/* Development-only admin API (/api/admin/ai). Keys go in and never come out. */

export const CHAT_PROVIDERS = ["gemini", "anthropic", "openai"] as const
export type ChatProvider = (typeof CHAT_PROVIDERS)[number]

/** Names accepted by PUT/DELETE /api/admin/ai/keys/{name}: the chat providers plus the judge's own key. */
export type KeyName = ChatProvider | "judge"

/** Values accepted by PUT /api/admin/ai `provider`: "auto" picks the first of gemini, anthropic, openai with a key. */
export type ProviderChoice = ChatProvider | "none" | "auto"

export const PROVIDER_SELECTIONS = ["auto", "explicit"] as const
export type ProviderSelection = (typeof PROVIDER_SELECTIONS)[number]

export const KEY_SOURCES = ["runtime", "environment", "dotenv", "user_secrets", "appsettings", "none"] as const

export interface AdminAiStatus {
  /** The provider searches use now (resolved when the selection is automatic), or "none". */
  active_provider: string
  /** "auto": the first provider with a key; "explicit": a provider (or none) was chosen. */
  provider_selection: ProviderSelection
  ai_configured: boolean
  providers: AdminProviderStatus[]
  judge: AdminJudgeStatus
  /** Where runtime changes are kept, e.g. "memory (runtime changes are lost on restart)". */
  persistence: string
}

export interface AdminProviderStatus {
  name: string
  model: string
  active: boolean
  configured: boolean
  key_source: string
  /** "sha256:" + 8 hex characters, or null. */
  key_fingerprint: string | null
}

export interface AdminJudgeStatus {
  enabled: boolean
  model: string
  configured: boolean
  key_source: string
  key_fingerprint: string | null
  threshold: number
}

/** PUT /api/admin/ai body: any subset; omitted fields stay as they are. */
export interface AdminAiUpdate {
  provider?: ProviderChoice | null
  models?: Record<string, string> | null
  judge_enabled?: boolean | null
}

export interface AdminProbeResult {
  provider: string
  model: string
  ok: boolean
  status: string
  elapsed_ms: number
  input_tokens: number | null
  output_tokens: number | null
  detail: string | null
}

/* Errors: RFC 9457 problem+json with a stable `code` and `trace_id`. Branch on `code`, never on text. */

export const PROBLEM_CODES = [
  "validation_failed",
  "no_searchable_terms",
  "not_found",
  "method_not_allowed",
  "rate_limited",
  "catalog_bad_response",
  "catalog_unavailable",
  "catalog_timeout",
  "search_timeout",
  "internal_error",
] as const

export interface ProblemBody {
  type?: string | null
  title?: string | null
  status?: number | null
  detail?: string | null
  code?: string
  trace_id?: string
  /** Field messages keyed by C# member name: Q, Limit, Prefix, ApiKey, Provider, Models… */
  errors?: Record<string, string[]>
  hint?: string
}
