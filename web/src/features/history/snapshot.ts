import type { SearchCandidate, SearchResponse } from "@/api/types"

/**
 * The part of a search answer the results UI renders. A live response and a saved history entry both satisfy it,
 * so one set of result components shows either. Diagnostics (meta, tiers, interpretation) are deliberately absent:
 * history keeps only this allowlist.
 */
export type CandidateSnapshot = Pick<
  SearchCandidate,
  | "rank"
  | "work_id"
  | "title"
  | "subtitle"
  | "author"
  | "primary_authors"
  | "primary_authors_total"
  | "contributors"
  | "contributors_total"
  | "first_publish_year"
  | "edition_count"
  | "open_library_url"
  | "cover_url"
  | "explanation"
  | "second_opinion"
  | "matched_subjects"
>

export interface ResultSnapshot {
  query: string
  status: SearchResponse["status"]
  hint: string | null
  degraded_reasons: SearchResponse["degraded_reasons"]
  /** The subjects a topic request searched ("books about bears"); empty for a request for one particular book. */
  topics: string[]
  candidates: CandidateSnapshot[]
}

export function toSnapshot(response: SearchResponse): ResultSnapshot {
  return {
    query: response.query,
    status: response.status,
    hint: response.hint,
    degraded_reasons: [...response.degraded_reasons],
    topics: response.interpretation.intent === "topic" ? [...response.interpretation.topics] : [],
    candidates: response.candidates.map((c) => ({
      rank: c.rank,
      work_id: c.work_id,
      title: c.title,
      subtitle: c.subtitle,
      author: c.author,
      primary_authors: c.primary_authors.map(({ key, name, url }) => ({ key, name, url })),
      primary_authors_total: c.primary_authors_total,
      contributors: c.contributors.map(({ key, name, role }) => ({ key, name, role })),
      contributors_total: c.contributors_total,
      first_publish_year: c.first_publish_year,
      edition_count: c.edition_count,
      open_library_url: c.open_library_url,
      cover_url: c.cover_url,
      explanation: c.explanation,
      second_opinion: c.second_opinion,
      matched_subjects: [...c.matched_subjects],
    })),
  }
}
