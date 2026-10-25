import { Link } from "@tanstack/react-router"
import { ChevronLeftIcon, SearchIcon } from "lucide-react"
import type { ReactNode } from "react"
import { useDocumentTitle } from "@/lib/use-document-title"
import { DecisionFigure } from "./DecisionFigure"
import { ExplainFigure } from "./ExplainFigure"
import { TIERS } from "./examples"
import { Actor } from "./figure"
import { InterpretFigure } from "./InterpretFigure"
import { LadderFigure } from "./LadderFigure"
import { NormalizeFigure } from "./NormalizeFigure"
import { PipelineFigure } from "./PipelineFigure"
import { RankingFigure } from "./RankingFigure"
import { TypoFigure } from "./TypoFigure"

const SECTIONS = [
  { id: "overview", title: "One search, start to finish" },
  { id: "clean", title: "Clean the words" },
  { id: "read", title: "Read the meaning" },
  { id: "search", title: "Search Open Library" },
  { id: "rank", title: "Score and rank" },
  { id: "decide", title: "Decide" },
  { id: "explain", title: "Explain" },
  { id: "failures", title: "When something fails" },
  { id: "measured", title: "How well it works" },
]

/** A walk-through of the search pipeline for reviewers: real examples, each illustrated step by step. */
export function HowItWorksPage() {
  useDocumentTitle("How it works")

  return (
    <div className="mx-auto flex w-full max-w-4xl flex-col gap-14 px-4 pt-4 pb-24 sm:px-6 sm:pt-6">
      <header className="flex flex-col items-start gap-4">
        <Link
          to="/"
          className="-ml-2 inline-flex min-h-10 items-center gap-1 rounded-lg px-2 text-sm font-medium text-ink-secondary transition-colors duration-150 hover:bg-muted hover:text-ink pointer-coarse:min-h-11"
        >
          <ChevronLeftIcon aria-hidden="true" className="size-4" />
          Back to search
        </Link>
        <h1
          id="page-heading"
          tabIndex={-1}
          className="font-heading text-heading font-semibold tracking-tight outline-none"
        >
          How Find That Book works
        </h1>
        <p className="max-w-measure text-title text-pretty text-ink">
          You type whatever you remember about a book, in any order, typos included. Find That Book searches Open
          Library, picks the book you most likely mean, and says why.
        </p>
        <p className="max-w-measure text-pretty text-ink-secondary">
          Every example on this page was recorded from the running code. Each picture plays once when you scroll to it;
          click any numbered step to jump there, or pick another example.
        </p>

        <div className="mt-2 grid w-full gap-3 rounded-panel border border-line bg-surface p-4 shadow-panel sm:p-5">
          <p className="text-title font-semibold text-ink">AI interprets, code decides</p>
          <ul className="grid gap-2 text-sm text-ink sm:grid-cols-2 sm:gap-6">
            <li className="flex items-start gap-2">
              <Actor kind="ai" className="mt-0.5 shrink-0" />
              <span>Guesses what your words mean, and writes the sentence under each book.</span>
            </li>
            <li className="flex items-start gap-2">
              <Actor kind="code" className="mt-0.5 shrink-0" />
              <span>Cleans, searches, scores and chooses. It checks everything the AI says.</span>
            </li>
          </ul>
          <p className="text-sm text-ink-secondary">
            So if the AI is switched off, slow or wrong, you still get an answer, and it is never a book the AI made up.
          </p>
        </div>

        <nav aria-label="On this page" className="w-full">
          <ol className="flex flex-wrap gap-x-1 gap-y-1 text-sm">
            {SECTIONS.map((section, i) => (
              <li key={section.id}>
                <a
                  href={`#${section.id}`}
                  className="inline-flex min-h-9 items-center gap-1.5 rounded-lg px-2 text-ink-secondary transition-colors duration-150 hover:bg-muted hover:text-ink pointer-coarse:min-h-11"
                >
                  <span className="tabular-nums">{i}.</span>
                  {section.title}
                </a>
              </li>
            ))}
          </ol>
        </nav>
      </header>

      <Section id="overview" number={0} title="One search, start to finish">
        <p>
          Here is one real search, slowed down. It takes five steps, and two of them use the AI. Reading the query and
          the first Open Library search run at the same time, so the AI adds almost no waiting.
        </p>
        <PipelineFigure />
      </Section>

      <Section id="clean" number={1} title="Clean the words" actors={["code"]}>
        <p>
          People type books in a hundred ways. Before anything else, code turns the query into plain lowercase words, so
          that <i>Les Misérables</i>, <span className="font-mono">les miserables</span> and{" "}
          <span className="font-mono">LES MISERABLES</span> all look the same. Words that describe an edition, small
          words and years are set aside, not thrown away: they still count later.
        </p>
        <NormalizeFigure />
        <p className="text-sm text-ink-secondary">
          Book titles from Open Library get the same cleaning, so both sides compare like with like. A subtitle is split
          off (<i>The Hobbit, or There and Back Again</i> is matched on “the hobbit”), and <i>Nineteen Eighty-Four</i>{" "}
          becomes 1984, just like your query would.
        </p>
      </Section>

      <Section id="read" number={2} title="Read the meaning" actors={["ai", "code"]}>
        <p>
          Some queries name a character or a plot instead of a title. A language model reads the cleaned query and
          suggests a title, an author and characters. Its answer is only a suggestion: code checks every value against
          your own words and removes anything that adds a fact you didn’t give.
        </p>
        <InterpretFigure />
        <p className="text-sm text-ink-secondary">
          The model is whichever one has a key: Gemini first, then Claude, then OpenAI. The examples were recorded with
          gpt-4.1-mini. With no key, or once the daily spending cap is used up, simple rules read the query instead.
        </p>
      </Section>

      <Section id="search" number={3} title="Search Open Library" actors={["code"]}>
        <p>
          Code starts with one broad search. If the results are convincing, it stops. If not, it climbs a ladder of
          narrower searches, one rung at a time, only as far as it has to.
        </p>
        <LadderFigure />
      </Section>

      <Section id="rank" number={4} title="Score and rank" actors={["code"]}>
        <p>
          Every book that came back is compared with what you typed. First, words are matched while forgiving small
          typos, but never in a way that turns one book into another.
        </p>
        <TypoFigure />
        <p>
          Then each book gets a tier: the kind of evidence that links it to your words. Only tiers A and B can ever be
          called a best match.
        </p>
        <ol aria-label="Tiers" className="grid gap-2">
          {TIERS.map((tier) => (
            <li key={tier.tier} className="flex items-start gap-3 rounded-lg border border-line bg-surface px-3 py-2.5">
              <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-sage font-semibold text-teal">
                {tier.tier}
              </span>
              <span className="flex flex-col">
                <span className="font-medium text-ink">{tier.rule}</span>
                <span className="text-sm text-ink-secondary">{tier.example}</span>
              </span>
            </li>
          ))}
        </ol>
        <p>
          A score puts the books in order. It adds up the title match, the author, popularity (the number of editions
          and readers), a matching year and character names, then subtracts penalties.
        </p>
        <RankingFigure />
      </Section>

      <Section id="decide" number={5} title="Decide: one book, or a few" actors={["code"]}>
        <p>
          A ranked list is not yet an answer. Code says “Best match” only when three questions all get a yes. Otherwise
          it shows a few books that fit, often with a hint on how to narrow them down.
        </p>
        <DecisionFigure />
        <p className="text-sm text-ink-secondary">
          An optional AI second opinion exists (the “judge”). It is off by default: in testing it raised precision only
          slightly and cost 18 matches. Even when on, it can never reorder the list or replace the first book.
        </p>
      </Section>

      <Section id="explain" number={6} title="Explain the answer" actors={["ai", "code"]}>
        <p>
          Each book gets one sentence saying why it was chosen. Code collects the facts; the AI turns them into a
          readable sentence. Then code checks every word of it. A sentence with anything the facts don’t support is
          thrown away and replaced by one code writes itself.
        </p>
        <ExplainFigure />
      </Section>

      <Section id="failures" number={7} title="When something fails">
        <ul className="grid gap-3 sm:grid-cols-3">
          <Fact title="The AI is off, slow or broken">
            Simple rules read the query and sentences come from templates. The search still answers, and the response
            lists what fell back.
          </Fact>
          <Fact title="Open Library is slow">
            Each search has 25 seconds and at most 5 Open Library calls. Code ranks whatever came back in time.
          </Fact>
          <Fact title="Nothing fits">
            “No book matched”, with a hint such as “Check the spelling or add an author.” Never a guess dressed up as an
            answer.
          </Fact>
        </ul>
      </Section>

      <Section id="measured" number={8} title="How well it works">
        <p>Measured on 409 queries that were kept sealed until the final build, so nothing was tuned on them.</p>
        <div className="grid gap-3 sm:grid-cols-2">
          <Measure label="AI off, code only" top1="72.7%" matches="227" precision="98.2%" />
          <Measure label="With the AI (gpt-4.1-mini)" top1="82.3%" matches="300" precision="96.0%" />
        </div>
        <p className="text-sm text-ink-secondary">
          The AI puts the right book first about 10 points more often and gives 73 more confident answers, for a small
          drop in how often those are right. The full tables, including costs and the in-sample test sets, are in the
          README.
        </p>
      </Section>

      <div className="flex flex-col items-start gap-3 rounded-panel bg-sage px-5 py-6">
        <p className="text-title font-semibold text-ink">Try it yourself</p>
        <p className="max-w-measure text-ink">
          Type a half-remembered title, an author, or a character. Try one of the examples above.
        </p>
        <Link
          to="/"
          className="inline-flex min-h-11 items-center gap-2 rounded-lg bg-teal px-4 font-medium text-white transition-colors duration-150 hover:bg-teal-hover"
        >
          <SearchIcon aria-hidden="true" className="size-4" />
          Search for a book
        </Link>
      </div>
    </div>
  )
}

function Section({
  id,
  number,
  title,
  actors = [],
  children,
}: {
  id: string
  number: number
  title: string
  actors?: ("ai" | "code")[]
  children: ReactNode
}) {
  return (
    <section aria-labelledby={`${id}-heading`} className="flex scroll-mt-20 flex-col gap-5" id={id}>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <span
          aria-hidden="true"
          className="flex size-9 shrink-0 items-center justify-center rounded-full bg-ink text-sm font-semibold text-white tabular-nums"
        >
          {number}
        </span>
        <h2 id={`${id}-heading`} className="text-[1.3125rem] leading-tight font-semibold tracking-tight text-ink">
          {title}
        </h2>
        {actors.length > 0 && (
          <span className="flex gap-1.5">
            {actors.map((kind) => (
              <Actor key={kind} kind={kind} />
            ))}
          </span>
        )}
      </div>
      <div className="flex flex-col gap-4 text-pretty text-ink [&>p]:max-w-measure">{children}</div>
    </section>
  )
}

function Fact({ title, children }: { title: string; children: ReactNode }) {
  return (
    <li className="flex flex-col gap-1 rounded-lg border border-line bg-surface p-4">
      <span className="font-medium text-ink">{title}</span>
      <span className="text-sm text-ink-secondary">{children}</span>
    </li>
  )
}

function Measure({
  label,
  top1,
  matches,
  precision,
}: {
  label: string
  top1: string
  matches: string
  precision: string
}) {
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-line bg-surface p-4">
      <p className="font-medium text-ink">{label}</p>
      <dl className="grid grid-cols-2 gap-3">
        <div>
          <dt className="text-caption text-ink-secondary">First book shown is right</dt>
          <dd className="text-2xl font-semibold text-ink tabular-nums">{top1}</dd>
        </div>
        <div>
          <dt className="text-caption text-ink-secondary">Answered “Best match”</dt>
          <dd className="text-2xl font-semibold text-ink tabular-nums">
            {matches} <span className="text-sm font-normal text-ink-secondary">times, {precision} right</span>
          </dd>
        </div>
      </dl>
    </div>
  )
}
