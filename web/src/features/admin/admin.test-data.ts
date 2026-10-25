import type { AdminAiStatus, AdminProbeResult, AdminProviderStatus } from "@/api/types"

/** A status shaped like the live API's: Gemini active with a .env key, Anthropic without a key, the judge on OpenAI's key. */
export function adminStatus(change: (status: AdminAiStatus) => void = () => {}): AdminAiStatus {
  const status: AdminAiStatus = {
    active_provider: "gemini",
    provider_selection: "explicit",
    ai_configured: true,
    providers: [
      provider("gemini", "gemini-3.5-flash-lite", {
        active: true,
        key_source: "dotenv",
        key_fingerprint: "sha256:81536b1d",
      }),
      provider("anthropic", "claude-haiku-5-5", { configured: false, key_source: "none", key_fingerprint: null }),
      provider("openai", "gpt-4.1-mini", { key_source: "dotenv", key_fingerprint: "sha256:d37db5a7" }),
    ],
    judge: {
      enabled: false,
      model: "gpt-6-luna",
      configured: true,
      key_source: "dotenv",
      key_fingerprint: "sha256:d37db5a7",
      threshold: 0.9,
    },
    persistence: "memory (runtime changes are lost on restart)",
  }
  change(status)
  return status
}

export function providerIn(status: AdminAiStatus, name: string): AdminProviderStatus {
  const found = status.providers.find((p) => p.name === name)
  if (!found) throw new Error(`no ${name} in status`)
  return found
}

function provider(name: string, model: string, values: Partial<AdminProviderStatus>): AdminProviderStatus {
  return { name, model, active: false, configured: true, key_source: "dotenv", key_fingerprint: null, ...values }
}

export function probeResult(values: Partial<AdminProbeResult> = {}): AdminProbeResult {
  return {
    provider: "gemini",
    model: "gemini-3.5-flash-lite",
    ok: true,
    status: "ok",
    elapsed_ms: 640,
    input_tokens: 180,
    output_tokens: 32,
    detail: "title=The Hobbit; author=J. R. R. Tolkien",
    ...values,
  }
}

/** The API's 400 for a rejected key (keys never appear in it). */
export function keyRejected(message = "A key has no spaces or control characters.") {
  return {
    type: "/problems/validation-failed",
    title: "One or more validation errors occurred.",
    status: 400,
    errors: { ApiKey: [message] },
    code: "validation_failed",
    trace_id: "00-abc-01",
  }
}

export const notFound = {
  type: "/problems/not-found",
  title: "No such API endpoint",
  status: 404,
  detail: "See /scalar or /openapi/v1.json for the endpoints.",
  code: "not_found",
  trace_id: "00-def-01",
}
