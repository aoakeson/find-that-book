import { formatCount, formatList } from "@/lib/format"
import type { ResultSnapshot } from "./snapshot"

/** One factual line about a saved answer, straight from the API's decision and order: no re-ranking, no guessing. */
export function outcomeSummary(result: ResultSnapshot): string {
  const count = result.candidates.length
  if (result.topics.length > 0 && count > 0) return `Books about ${formatList(result.topics)}: ${formatCount(count)}`
  if (result.status === "match" && count > 0) return `Best match: ${result.candidates[0].title}`
  if (result.status === "ambiguous" && count > 0)
    return count === 1 ? "1 possible book" : `${formatCount(count)} possible books`
  return "No match"
}
