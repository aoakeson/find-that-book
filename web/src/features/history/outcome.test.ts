import { expect, it } from "vitest"
import { fixtures } from "@/test/fixtures"
import { outcomeSummary } from "./outcome"
import { toSnapshot } from "./snapshot"

it("summarises each outcome from the API's own status and order", () => {
  const match = toSnapshot(fixtures.match())
  expect(outcomeSummary(match)).toBe(`Best match: ${match.candidates[0].title}`)

  const ambiguous = toSnapshot(fixtures.ambiguous())
  expect(outcomeSummary(ambiguous)).toBe(`${ambiguous.candidates.length} possible books`)
  expect(outcomeSummary({ ...ambiguous, candidates: ambiguous.candidates.slice(0, 1) })).toBe("1 possible book")

  expect(outcomeSummary(toSnapshot(fixtures.none()))).toBe("No match")
})

it("never claims a match it can't name", () => {
  expect(outcomeSummary({ ...toSnapshot(fixtures.match()), candidates: [] })).toBe("No match")
  expect(outcomeSummary({ ...toSnapshot(fixtures.ambiguous()), candidates: [] })).toBe("No match")
})
