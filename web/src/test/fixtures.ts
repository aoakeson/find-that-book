import type { AdminAiStatus, HealthResponse, ProblemBody, SearchResponse, SuggestResponse } from "@/api/types"

/**
 * The API's own OpenAPI examples (src/FindThatBook.Api/OpenApi/Examples). The backend's drift test deserializes each
 * of them into the real C# contract types, so using them here ties the frontend tests to the actual wire format.
 */
const files = import.meta.glob<{ summary: string; value: unknown }>(
  "../../../src/FindThatBook.Api/OpenApi/Examples/*.json",
  {
    eager: true,
    import: "default",
  }
)

function example<T>(name: string): T {
  const key = Object.keys(files).find((k) => k.endsWith(`/${name}.json`))
  if (!key) throw new Error(`No API example named ${name}`)
  return structuredClone(files[key].value) as T
}

export const fixtures = {
  match: () => example<SearchResponse>("SearchBooks.200.clear_match"),
  contributorMatch: () => example<SearchResponse>("SearchBooks.200.contributor_match"),
  ambiguous: () => example<SearchResponse>("SearchBooks.200.ambiguous_author_only"),
  degraded: () => example<SearchResponse>("SearchBooks.200.degraded_llm_down"),
  none: () => example<SearchResponse>("SearchBooks.200.none"),
  noSearchableTerms: () => example<ProblemBody>("SearchBooks.400.no_searchable_terms"),
  validationFailed: () => example<ProblemBody>("SearchBooks.400.validation_failed"),
  rateLimited: () => example<ProblemBody>("SearchBooks.429.rate_limited"),
  catalogBadResponse: () => example<ProblemBody>("SearchBooks.502.catalog_bad_response"),
  catalogUnavailable: () => example<ProblemBody>("SearchBooks.503.catalog_unavailable"),
  catalogTimeout: () => example<ProblemBody>("SearchBooks.504.catalog_timeout"),
  searchTimeout: () => example<ProblemBody>("SearchBooks.504.search_timeout"),
  /** Harry Potter titles first. The backend renamed this example (prefix_har → prefix_harry); the name stays for callers. */
  suggestHar: () => example<SuggestResponse>("SuggestBooks.200.prefix_harry"),
  suggestEmpty: () => example<SuggestResponse>("SuggestBooks.200.empty_prefix"),
  health: () => example<HealthResponse>("Health.200.ok"),
}

/** Every 200 search example, for parameterised tests. */
export const searchExamples = () =>
  Object.entries(files)
    .filter(([k]) => /SearchBooks\.200\./.test(k))
    .map(([k, v]) => ({
      name: k.split("/").pop()!.replace(".json", ""),
      response: structuredClone(v.value) as SearchResponse,
    }))

/** Every API example file, untyped: "SearchBooks.400.validation_failed" → its value. For the contract test. */
export const apiExamples = () =>
  Object.entries(files).map(([k, v]) => ({
    name: k.split("/").pop()!.replace(".json", ""),
    value: structuredClone(v.value),
  }))

/**
 * GET /api/admin/ai has no example file (it reflects local configuration), so this one is hand-written; contract.test.ts
 * checks its shape against the OpenAPI schema. Fingerprints are fake; no real key is involved anywhere.
 */
export function adminStatus(overrides: Partial<AdminAiStatus> = {}): AdminAiStatus {
  return {
    active_provider: "gemini",
    provider_selection: "explicit",
    ai_configured: true,
    providers: [
      {
        name: "gemini",
        model: "gemini-3.5-flash-lite",
        active: true,
        configured: true,
        key_source: "dotenv",
        key_fingerprint: "sha256:1a2b3c4d",
      },
      {
        name: "anthropic",
        model: "claude-haiku-5-5",
        active: false,
        configured: false,
        key_source: "none",
        key_fingerprint: null,
      },
      {
        name: "openai",
        model: "gpt-4.1-mini",
        active: false,
        configured: true,
        key_source: "runtime",
        key_fingerprint: "sha256:9f8e7d6c",
      },
    ],
    judge: {
      enabled: true,
      model: "gpt-6-luna",
      configured: true,
      key_source: "environment",
      key_fingerprint: "sha256:0a1b2c3d",
      threshold: 0.9,
    },
    persistence: "memory (runtime changes are lost on restart)",
    ...overrides,
  }
}
