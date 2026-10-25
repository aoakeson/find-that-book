/** Shared display formatting. Locale-aware through Intl; no library. */

const numberFormat = new Intl.NumberFormat()

export function formatCount(value: number): string {
  return numberFormat.format(value)
}

/** 840 → "840 ms", 2530 → "2.5 s", 61000 → "61 s". */
export function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return "–"
  if (ms < 1000) return `${Math.round(ms)} ms`
  const seconds = ms / 1000
  return seconds < 10 ? `${seconds.toFixed(1)} s` : `${Math.round(seconds)} s`
}

const list = new Intl.ListFormat(undefined, { style: "long", type: "conjunction" })

/** ["bears", "dragons"] → "bears and dragons". */
export function formatList(items: readonly string[]): string {
  return list.format(items)
}

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" })
const absolute = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" })

/** "just now", "5 minutes ago", "yesterday"; older than a week → an absolute date. */
export function formatRelativeTime(iso: string, now = Date.now()): string {
  const time = Date.parse(iso)
  if (Number.isNaN(time)) return ""
  const seconds = Math.round((time - now) / 1000)
  const abs = Math.abs(seconds)
  if (abs < 45) return "just now"
  if (abs < 3600) return relative.format(Math.round(seconds / 60), "minute")
  if (abs < 86_400) return relative.format(Math.round(seconds / 3600), "hour")
  if (abs < 7 * 86_400) return relative.format(Math.round(seconds / 86_400), "day")
  return absolute.format(time)
}

export function formatDateTime(iso: string): string {
  const time = Date.parse(iso)
  return Number.isNaN(time) ? "" : absolute.format(time)
}

/**
 * Only http(s) links from the API reach an href or src. Anything else (javascript:, data:, relative junk) is dropped.
 * The API builds these from configured Open Library hosts, so the host is not pinned here.
 */
export function safeExternalUrl(value: string | null | undefined): string | null {
  if (!value) return null
  try {
    const url = new URL(value)
    return url.protocol === "https:" || url.protocol === "http:" ? url.href : null
  } catch {
    return null
  }
}
