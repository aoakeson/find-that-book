/*
 * Keeps src/api/types.ts in step with the API:
 * - public types against the API's OpenAPI document (openapi.snapshot.json; `npm run api:snapshot` refreshes it);
 * - admin types against AdminContracts.cs (the Development-only admin API is left out of the OpenAPI document);
 * - every API example file against the types, key by key.
 * Each field is written below as a short descriptor ("string|null", "SearchCandidate[]"…). The compiler derives the
 * only allowed descriptor from types.ts, so a missing, extra or retyped field there is a type error here; a backend
 * change makes the runtime comparison fail.
 */
import { describe, expect, expectTypeOf, it } from "vitest"
import adminContractsCs from "../../../src/FindThatBook.Api/Contracts/AdminContracts.cs?raw"
import { adminStatus, apiExamples, fixtures } from "@/test/fixtures"
import openapiJson from "./openapi.snapshot.json?raw"
import * as T from "./types"

/* ---- 1. What types.ts says, as descriptors the compiler checks ---- */

/** Every object the UI reads or sends, by its contract name. */
interface Objects {
  SearchResponse: T.SearchResponse
  SearchInterpretation: T.SearchInterpretation
  SearchCandidate: T.SearchCandidate
  AuthorLink: T.AuthorLink
  ContributorLink: T.ContributorLink
  SearchMeta: T.SearchMeta
  OpenLibraryMeta: T.OpenLibraryMeta
  CatalogStep: T.CatalogStep
  LlmMeta: T.LlmMeta
  LlmStep: T.LlmStep
  JudgeMeta: T.JudgeMeta
  SuggestResponse: T.SuggestResponse
  SuggestionItem: T.SuggestionItem
  HealthResponse: T.HealthResponse
  SuggestIndexInfo: T.SuggestIndexInfo
  UpstreamHealth: T.UpstreamHealth
  AdminAiStatus: T.AdminAiStatus
  AdminProviderStatus: T.AdminProviderStatus
  AdminJudgeStatus: T.AdminJudgeStatus
  AdminProbeResult: T.AdminProbeResult
  AdminAiUpdate: T.AdminAiUpdate
}

interface Enums {
  MatchStatus: T.MatchStatus
  MatchTier: T.MatchTier
  StatusReason: T.StatusReason
  DegradedReason: T.DegradedReason
  InterpretationSource: T.InterpretationSource
  QueryIntent: T.QueryIntent
  ExplanationSource: T.ExplanationSource
  JudgeStatus: T.JudgeStatus
  CatalogCallOutcome: T.CatalogCallOutcome
  LlmCallStatus: T.LlmCallStatus
  SuggestMatch: T.SuggestMatch
}

type Named = Objects & Enums
type Equals<A, B> = (<X>() => X extends A ? 1 : 2) extends <X>() => X extends B ? 1 : 2 ? true : false
/** "SearchCandidate" for T.SearchCandidate, never for anything else. */
type NameOf<V> = { [N in keyof Named]: Equals<Named[N], V> extends true ? N : never }[keyof Named]

/**
 * A field as "string" | "number" | "boolean" | a contract name | "X[]" | "map<X>", plus "|null" when null is allowed
 * and a leading "?" when the field may be absent. A string union that isn't a named enum (e.g. ProviderChoice) is
 * "string", as in the schema. Spelled out one level deep (the contract has nothing deeper): tsc rejects a recursive one.
 */
type Scalar<V> = [NameOf<V>] extends [never]
  ? V extends string
    ? "string"
    : V extends number
      ? "number"
      : V extends boolean
        ? "boolean"
        : never
  : NameOf<V>
type Nullable<V, S extends string> = null extends V ? `${S}|null` : S
type Item<V> = Nullable<V, Scalar<NonNullable<V>>>
type Shape<V> = [NameOf<V>] extends [never]
  ? V extends readonly (infer I)[]
    ? `${Item<I>}[]`
    : V extends string | number | boolean
      ? Scalar<V>
      : V extends Record<string, infer I>
        ? I extends readonly (infer J)[]
          ? `map<${Item<J>}[]>`
          : `map<${Item<I>}>`
        : never
  : NameOf<V>
type Field<V> = Nullable<V, Shape<NonNullable<V>>>
type Spec<O> = {
  [K in keyof O]-?: Partial<Pick<O, K>> extends Pick<O, K> ? `?${Field<Exclude<O[K], undefined>>}` : Field<O[K]>
}

const objects: { [N in keyof Objects]: Spec<Objects[N]> } = {
  SearchResponse: {
    query: "string",
    status: "MatchStatus",
    status_reason: "StatusReason",
    hint: "string|null",
    degraded: "boolean",
    degraded_reasons: "DegradedReason[]",
    interpretation: "SearchInterpretation",
    candidates: "SearchCandidate[]",
    meta: "SearchMeta",
  },
  SearchInterpretation: {
    title: "string|null",
    author: "string|null",
    characters: "string[]",
    keywords: "string[]",
    english_title: "string|null",
    original_title: "string|null",
    year: "number|null",
    removed_noise: "string[]",
    source: "InterpretationSource",
    intent: "QueryIntent",
    topics: "string[]",
  },
  SearchCandidate: {
    rank: "number",
    work_id: "string",
    title: "string",
    subtitle: "string|null",
    author: "string|null",
    primary_authors: "AuthorLink[]",
    primary_authors_total: "number",
    contributors: "ContributorLink[]",
    contributors_total: "number",
    first_publish_year: "number|null",
    edition_count: "number",
    open_library_url: "string",
    cover_url: "string|null",
    match_tier: "MatchTier",
    match_tier_label: "string",
    matched_subjects: "string[]",
    explanation: "string",
    explanation_source: "ExplanationSource",
    second_opinion: "boolean",
    duplicates_folded: "number",
  },
  AuthorLink: { key: "string|null", name: "string", url: "string|null" },
  ContributorLink: { key: "string|null", name: "string", role: "string|null" },
  SearchMeta: {
    request_id: "string",
    elapsed_ms: "number",
    timings_ms: "map<number>",
    open_library: "OpenLibraryMeta",
    llm: "LlmMeta",
    judge: "JudgeMeta",
  },
  OpenLibraryMeta: {
    calls: "number",
    cache_hits: "number",
    upstream_requests: "number",
    retries: "number",
    slow_empty_requeries: "number",
    budget: "number",
    steps: "CatalogStep[]",
  },
  CatalogStep: {
    step: "string",
    outcome: "CatalogCallOutcome",
    cache_hit: "boolean",
    attempts: "number",
    elapsed_ms: "number",
    results: "number|null",
    num_found: "number|null",
  },
  LlmMeta: {
    provider: "string|null",
    model: "string|null",
    calls: "number",
    cache_hits: "number",
    input_tokens: "number|null",
    output_tokens: "number|null",
    est_cost_usd: "number|null",
    prompt_versions: "map<string>",
    steps: "LlmStep[]",
  },
  LlmStep: {
    operation: "string",
    provider: "string",
    model: "string",
    prompt_id: "string",
    status: "LlmCallStatus",
    cache_hit: "boolean",
    elapsed_ms: "number",
    input_tokens: "number|null",
    output_tokens: "number|null",
  },
  JudgeMeta: {
    enabled: "boolean",
    status: "JudgeStatus",
    pick: "string|null",
    probability: "number|null",
    threshold: "number",
    agreed: "boolean|null",
    model: "string|null",
    latency_ms: "number|null",
    input_tokens: "number|null",
    est_cost_usd: "number|null",
    detail: "string|null",
  },
  SuggestResponse: { prefix: "string", suggestions: "SuggestionItem[]" },
  SuggestionItem: {
    work_id: "string",
    title: "string",
    author: "string|null",
    first_publish_year: "number|null",
    cover_url: "string|null",
    edition_count: "number",
    open_library_url: "string",
    match: "SuggestMatch",
  },
  HealthResponse: {
    status: "string",
    version: "string",
    environment: "string",
    ai_provider: "string|null",
    ai_configured: "boolean|null",
    judge_enabled: "boolean|null",
    suggest_index: "SuggestIndexInfo|null",
    upstream: "UpstreamHealth|null",
  },
  SuggestIndexInfo: { version: "string", count: "number" },
  UpstreamHealth: {
    open_library_circuit: "string|null",
    open_library_last_failure: "string|null",
    open_library_last_failure_reason: "string|null",
    llm_status: "string|null",
    llm_last_failure: "string|null",
    llm_last_failure_reason: "string|null",
  },
  AdminAiStatus: {
    active_provider: "string",
    provider_selection: "string",
    ai_configured: "boolean",
    providers: "AdminProviderStatus[]",
    judge: "AdminJudgeStatus",
    persistence: "string",
  },
  AdminProviderStatus: {
    name: "string",
    model: "string",
    active: "boolean",
    configured: "boolean",
    key_source: "string",
    key_fingerprint: "string|null",
  },
  AdminJudgeStatus: {
    enabled: "boolean",
    model: "string",
    configured: "boolean",
    key_source: "string",
    key_fingerprint: "string|null",
    threshold: "number",
  },
  AdminProbeResult: {
    provider: "string",
    model: "string",
    ok: "boolean",
    status: "string",
    elapsed_ms: "number",
    input_tokens: "number|null",
    output_tokens: "number|null",
    detail: "string|null",
  },
  AdminAiUpdate: { provider: "?string|null", models: "?map<string>|null", judge_enabled: "?boolean|null" },
}

const enums = {
  MatchStatus: T.MATCH_STATUSES,
  MatchTier: T.MATCH_TIERS,
  StatusReason: T.STATUS_REASONS,
  DegradedReason: T.DEGRADED_REASONS,
  InterpretationSource: T.INTERPRETATION_SOURCES,
  QueryIntent: T.QUERY_INTENTS,
  ExplanationSource: T.EXPLANATION_SOURCES,
  JudgeStatus: T.JUDGE_STATUSES,
  CatalogCallOutcome: T.CATALOG_CALL_OUTCOMES,
  LlmCallStatus: T.LLM_CALL_STATUSES,
  SuggestMatch: T.SUGGEST_MATCHES,
} satisfies { [N in keyof Enums]: readonly Enums[N][] }

/** Errors: type/title/status/detail/errors are in the schema; code, trace_id and hint are extensions, checked on examples. */
const problemBody: Spec<T.ProblemBody> = {
  type: "?string|null",
  title: "?string|null",
  status: "?number|null",
  detail: "?string|null",
  errors: "?map<string[]>",
  code: "?string",
  trace_id: "?string",
  hint: "?string",
}

/* ---- 2. What the API says, as the same descriptors ---- */

interface JsonSchema {
  $ref?: string
  type?: string | string[]
  enum?: unknown[]
  oneOf?: JsonSchema[]
  items?: JsonSchema
  additionalProperties?: JsonSchema | boolean
  properties?: Record<string, JsonSchema>
  required?: string[]
}

const schemas = (JSON.parse(openapiJson) as { components: { schemas: Record<string, JsonSchema> } }).components.schemas

/** One OpenAPI property. .NET types numbers as ["integer", "string"] (it can read them from strings): that's a number. */
function describeSchema(schema: JsonSchema): string {
  if (schema.$ref) return schema.$ref.replace("#/components/schemas/", "")
  if (schema.oneOf) {
    const [other, ...more] = schema.oneOf.filter((v) => v.type !== "null")
    if (!other || more.length > 0) throw new Error(`Unsupported schema ${JSON.stringify(schema)}`)
    return schema.oneOf.length > 1 ? `${describeSchema(other)}|null` : describeSchema(other)
  }
  const types = [schema.type ?? []].flat()
  const rest = types.filter((t) => t !== "null")
  let base: string
  if (rest.includes("integer") || rest.includes("number")) base = "number"
  else if (rest.length === 1 && (rest[0] === "string" || rest[0] === "boolean")) base = rest[0]
  else if (rest[0] === "array" && schema.items) base = `${describeSchema(schema.items)}[]`
  else if (rest[0] === "object" && typeof schema.additionalProperties === "object")
    base = `map<${describeSchema(schema.additionalProperties)}>`
  else throw new Error(`Unsupported schema ${JSON.stringify(schema)}`)
  return types.includes("null") ? `${base}|null` : base
}

function describeObject(schema: JsonSchema): Record<string, string> {
  const required = new Set(schema.required ?? [])
  return Object.fromEntries(
    Object.entries(schema.properties ?? {}).map(([k, p]) => [k, `${required.has(k) ? "" : "?"}${describeSchema(p)}`])
  )
}

/** One C# property type: string?, bool, long, IReadOnlyList<X>, IReadOnlyDictionary<string, X> or a record name. */
function describeCSharpType(type: string): string {
  const bare = type.replace(/\?$/, "")
  const list = /^IReadOnlyList<(.+)>$/.exec(bare)
  const map = /^IReadOnlyDictionary<string, (.+)>$/.exec(bare)
  let base: string
  if (list) base = `${describeCSharpType(list[1])}[]`
  else if (map) base = `map<${describeCSharpType(map[1])}>`
  else if (bare === "string") base = "string"
  else if (bare === "bool") base = "boolean"
  else if (/^(int|long|double)$/.test(bare)) base = "number"
  else if (/^[A-Z]\w*$/.test(bare)) base = bare
  else throw new Error(`Unsupported C# type ${type}`)
  return type.endsWith("?") ? `${base}|null` : base
}

/**
 * `public sealed record Name { [Attr] public (required) Type Member { get; init; } … }` → descriptors, with names in
 * the API's snake_case JSON policy. `required` or [Required] makes a field required.
 */
function describeCSharpRecords(source: string): Record<string, Record<string, string>> {
  const records: Record<string, Record<string, string>> = {}
  for (const [, name, body] of source.matchAll(/public sealed record (\w+)\s*\{([\s\S]*?)\n\}/g)) {
    const properties = body.matchAll(/((?:\[[^\]]*\]\s*)*)public (required )?([\w<>?, ]+?) (\w+) \{ get; init; \}/g)
    records[name] = Object.fromEntries(
      [...properties].map(([, attributes, required, type, member]) => [
        member.replace(/([a-z0-9])([A-Z])/g, "$1_$2").toLowerCase(),
        `${required || /\[Required\b/.test(attributes) ? "" : "?"}${describeCSharpType(type)}`,
      ])
    )
  }
  return records
}

const adminRecords = describeCSharpRecords(adminContractsCs)

/* ---- 3. Checking a JSON value (an API example) against the descriptors ---- */

function checkValue(value: unknown, field: string, path: string, problems: string[]) {
  const descriptor = field.replace(/^\?/, "")
  if (value === null) {
    if (!descriptor.endsWith("|null")) problems.push(`${path} is null, but types.ts says ${descriptor}`)
    return
  }
  const type = descriptor.replace(/\|null$/, "")
  if (type.endsWith("[]")) {
    if (Array.isArray(value)) value.forEach((item, i) => checkValue(item, type.slice(0, -2), `${path}[${i}]`, problems))
    else problems.push(`${path} should be an array`)
  } else if (type.startsWith("map<")) {
    if (isPlainObject(value))
      Object.entries(value).forEach(([k, v]) => checkValue(v, type.slice(4, -1), `${path}.${k}`, problems))
    else problems.push(`${path} should be an object`)
  } else if (type in enums) {
    if (!(enums[type as keyof Enums] as readonly unknown[]).includes(value))
      problems.push(`${path} is ${JSON.stringify(value)}, not a ${type} in types.ts`)
  } else if (type in objects) {
    checkObject(value, objects[type as keyof Objects], path, problems)
  } else if (typeof value !== type) {
    problems.push(`${path} is ${JSON.stringify(value)}, but types.ts says ${type}`)
  }
}

function checkObject(value: unknown, spec: Record<string, string>, path: string, problems: string[]) {
  if (!isPlainObject(value)) {
    problems.push(`${path} should be an object`)
    return
  }
  for (const key of Object.keys(value))
    if (!(key in spec)) problems.push(`${path}.${key} is in the API's example but not in types.ts`)
  for (const [key, field] of Object.entries(spec)) {
    if (key in value) checkValue(value[key], field, `${path}.${key}`, problems)
    else if (!field.startsWith("?")) problems.push(`${path}.${key} is in types.ts but missing from the API's example`)
  }
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
}

const isProblemExample = (name: string) => /\.[45]\d\d\./.test(name)

function exampleSpec(name: string): Record<string, string> {
  if (isProblemExample(name)) return problemBody
  if (name.startsWith("SearchBooks.200.")) return objects.SearchResponse
  if (name.startsWith("SuggestBooks.200.")) return objects.SuggestResponse
  if (name.startsWith("Health.200.")) return objects.HealthResponse
  throw new Error(`No type for the API example ${name}: add it to exampleSpec()`)
}

/* ---- Tests ---- */

describe("types.ts matches the API's contract", () => {
  it.each(Object.keys(objects) as (keyof Objects)[])(
    "%s has the same fields, types, nullability and required list",
    (name) => {
      const contract = schemas[name] ? describeObject(schemas[name]) : adminRecords[name]
      expect(contract, `Neither the OpenAPI document nor AdminContracts.cs defines ${name}`).toBeDefined()
      expect(contract).toEqual(objects[name])
    }
  )

  it.each(Object.keys(enums) as (keyof Enums)[])("%s lists the same values", (name) => {
    expect([...enums[name]].sort()).toEqual([...(schemas[name]?.enum ?? [])].map(String).sort())
  })

  it("derives every enum type from its const array, and covers every enum the document defines", () => {
    expectTypeOf<{ [N in keyof Enums]: (typeof enums)[N][number] }>().toEqualTypeOf<Enums>()
    const documented = Object.keys(schemas).filter((n) => Array.isArray(schemas[n].enum))
    expect(documented.sort()).toEqual(Object.keys(enums).sort())
  })

  it("reads the standard problem fields the way the document types them", () => {
    const standard = {
      ...describeObject(schemas.ProblemDetails),
      ...describeObject(schemas.HttpValidationProblemDetails),
    }
    delete standard.instance // not read by the UI
    expect(standard).toEqual({
      type: problemBody.type,
      title: problemBody.title,
      status: problemBody.status,
      detail: problemBody.detail,
      errors: problemBody.errors,
    })
  })

  it("sends the admin key in the one required field AdminSetKey reads", () => {
    expect(adminRecords.AdminSetKey).toEqual({ api_key: "string|null" })
  })
})

describe("the API's example files have exactly the fields types.ts declares", () => {
  const examples = apiExamples()

  it("finds the example files", () => {
    expect(examples.length).toBeGreaterThanOrEqual(10)
  })

  it.each(examples)("$name", ({ name, value }) => {
    const problems: string[] = []
    checkObject(value, exampleSpec(name), name, problems)
    expect(problems).toEqual([])
  })

  it.each(examples.filter((e) => isProblemExample(e.name)))(
    "$name carries a known code and a trace id",
    ({ name, value }) => {
      const body = value as T.ProblemBody
      expect(T.PROBLEM_CODES).toContain(body.code)
      expect(body.trace_id).toEqual(expect.any(String))
      expect(String(body.status)).toBe(name.split(".")[1])
    }
  )

  it("the checker itself reports extra, missing, null and unknown enum values", () => {
    const candidate: Record<string, unknown> = {
      ...fixtures.match().candidates[0],
      extra: 1,
      title: null,
      match_tier: "z",
    }
    delete candidate.rank
    const problems: string[] = []
    checkObject(candidate, objects.SearchCandidate, "c", problems)
    expect(problems).toEqual([
      "c.extra is in the API's example but not in types.ts",
      "c.rank is in types.ts but missing from the API's example",
      "c.title is null, but types.ts says string",
      'c.match_tier is "z", not a MatchTier in types.ts',
    ])
  })

  it("the hand-written admin status fixture matches too", () => {
    const problems: string[] = []
    checkObject(adminStatus(), objects.AdminAiStatus, "adminStatus()", problems)
    expect(problems).toEqual([])
  })
})
