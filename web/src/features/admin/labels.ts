import { CHAT_PROVIDERS, type ChatProvider, type KeyName, type ProviderChoice } from "@/api/types"

const PROVIDER_NAMES: Record<string, string> = { gemini: "Gemini", anthropic: "Anthropic", openai: "OpenAI" }

/** "gemini" → "Gemini". An unknown name from a newer API is shown as sent. */
export function providerName(name: string): string {
  return PROVIDER_NAMES[name] ?? name
}

export function isChatProvider(name: string): name is ChatProvider {
  return (CHAT_PROVIDERS as readonly string[]).includes(name)
}

export const PROVIDER_CHOICES: ReadonlyArray<{ value: ProviderChoice; label: string }> = [
  { value: "auto", label: "Automatic (first with a key)" },
  { value: "gemini", label: "Gemini" },
  { value: "anthropic", label: "Anthropic" },
  { value: "openai", label: "OpenAI" },
  { value: "none", label: "None (AI off)" },
]

export interface KeyLabels {
  /** Row heading. */
  title: string
  /** Used in field labels, button names and toasts: "Gemini API key", "Gemini key saved". */
  short: string
}

export function keyLabels(name: KeyName): KeyLabels {
  return name === "judge"
    ? { title: "Second opinion (OpenAI Decisions)", short: "Second opinion" }
    : { title: providerName(name), short: providerName(name) }
}

/** Where the key in use came from, in words. */
export function keySourceWords(configured: boolean, source: string): string {
  if (!configured || source === "none") return "Not set"
  switch (source) {
    case "runtime":
      return "Set here until the API restarts"
    case "dotenv":
      return "Key set from .env"
    case "environment":
      return "Key set from an environment variable"
    case "user_secrets":
      return "Key set from user secrets"
    case "appsettings":
      return "Key set from appsettings"
    default:
      return "Key set"
  }
}

/** A failed probe's `status` (the API's LLM call statuses), in words. */
export function probeStatusWords(status: string): string {
  switch (status) {
    case "not_configured":
      return "No usable key: none is set, or the provider turned it down earlier."
    case "budget_exhausted":
      return "The AI budget for this minute or day is used up."
    case "timeout":
      return "The provider didn't answer in time."
    case "rate_limited":
      return "The provider is limiting requests right now."
    case "provider_error":
      return "The provider returned an error."
    case "malformed_output":
      return "The provider's answer couldn't be read."
    case "truncated":
      return "The provider's answer was cut off."
    case "refused":
      return "The provider refused the request."
    default:
      return `The test failed (${status}).`
  }
}
