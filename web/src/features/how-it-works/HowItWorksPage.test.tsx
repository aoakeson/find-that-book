import { act, fireEvent, render, screen, within } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"
import { REDUCED_MOTION_QUERY } from "@/lib/motion"
import { fakeApi, json } from "@/test/fake-api"
import { fixtures } from "@/test/fixtures"
import { renderApp } from "@/test/render"
import { ExplainFigure } from "./ExplainFigure"
import { TEMPLATE_SENTENCE } from "./examples"

function stubReducedMotion() {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query === REDUCED_MOTION_QUERY,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  }))
}

const stages = () => screen.getByRole("list", { name: "Explanation stages" })
const currentStage = () => within(stages()).getByRole("button", { current: "step" })
/** Each step's timer starts after the previous step renders, so time has to pass one step at a time. */
function playThrough() {
  for (let i = 0; i < 8; i++) act(() => vi.advanceTimersByTime(4000))
}

afterEach(() => {
  vi.useRealTimers()
})

describe("an illustration", () => {
  it("plays its steps in order, pauses on request, and jumps to a clicked step", () => {
    vi.useFakeTimers()
    render(<ExplainFigure />)
    expect(currentStage()).toHaveTextContent("Facts")

    act(() => vi.advanceTimersByTime(1600))
    expect(currentStage()).toHaveTextContent("AI writes")

    fireEvent.click(screen.getByRole("button", { name: "Pause animation" }))
    playThrough()
    expect(currentStage()).toHaveTextContent("AI writes")

    fireEvent.click(within(stages()).getByRole("button", { name: /Shown/ }))
    expect(currentStage()).toHaveTextContent("Shown")
    expect(screen.getByRole("button", { name: "Play animation" })).toBeInTheDocument()
  })

  it("replays from the first step when another example is picked, and offers Replay at the end", () => {
    vi.useFakeTimers()
    render(<ExplainFigure />)
    playThrough()
    expect(currentStage()).toHaveTextContent("Shown")
    expect(screen.getByRole("button", { name: "Replay animation" })).toBeInTheDocument()

    const praise = screen.getByRole("button", { name: "Invented praise" })
    fireEvent.click(praise)
    expect(praise).toHaveAttribute("aria-pressed", "true")
    expect(currentStage()).toHaveTextContent("Facts")
    // Every word of the figure is in the DOM from the start, so assistive technology reads the whole picture.
    expect(screen.getByText("“beloved”, “fantasy” and “classic” are not in the facts")).toBeInTheDocument()
    expect(screen.getByText(TEMPLATE_SENTENCE)).toBeInTheDocument()
  })

  it("with reduced motion shows the last step at once, with no play control, but still jumps to a clicked step", () => {
    stubReducedMotion()
    render(<ExplainFigure />)
    expect(currentStage()).toHaveTextContent("Shown")
    expect(screen.queryByRole("button", { name: /animation$/ })).not.toBeInTheDocument()

    fireEvent.click(within(stages()).getByRole("button", { name: /AI writes/ }))
    expect(currentStage()).toHaveTextContent("AI writes")
  })
})

describe("the page", () => {
  it("covers every part of the pipeline in order, each figure finished under reduced motion", async () => {
    stubReducedMotion()
    fakeApi({
      "GET /api/health": () => json(fixtures.health()),
      "GET /api/suggest": () => json(fixtures.suggestEmpty()),
    })
    await renderApp("/how-it-works")
    expect(await screen.findByRole("heading", { level: 1, name: "How Find That Book works" })).toBeInTheDocument()

    const sections = screen.getAllByRole("heading", { level: 2 }).map((h) => h.textContent)
    expect(sections).toEqual([
      "One search, start to finish",
      "Clean the words",
      "Read the meaning",
      "Search Open Library",
      "Score and rank",
      "Decide: one book, or a few",
      "Explain the answer",
      "When something fails",
      "How well it works",
    ])
    const figures = document.querySelectorAll("figure")
    expect(figures).toHaveLength(8)
    for (const figure of figures) {
      // The stage track is the figure's first list; its last stage is the current one.
      const track = figure.querySelector<HTMLElement>("ol[aria-label]")!
      expect(within(track).getAllByRole("button").at(-1)).toHaveAttribute("aria-current", "step")
    }
  })
})
