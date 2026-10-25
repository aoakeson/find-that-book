# Agent guide

Start at [okf/index.md](okf/index.md) and read [Product](okf/product.md). Open the concept for the task, follow its `sources` to the detailed project docs, and inspect the current code before editing. The OKF bundle is a short map; the linked docs and code carry the detail. Check `git status` first and preserve unrelated work. If a source and code disagree, resolve the difference in the same change.

## Task router

| Task | Read first | Edit where the behavior belongs |
| --- | --- | --- |
| Search, ranking, status, explanation facts | [Server](okf/server.md) | Pure policy and orchestration in `src/FindThatBook.Core`; adapters in `src/FindThatBook.Infrastructure` |
| Routes, validation, configuration, Open Library | [Server](okf/server.md) | `src/FindThatBook.Api` for HTTP; `Infrastructure` for external I/O |
| Pages, interaction, styling, accessibility | [Frontend](okf/frontend.md) | `web/src`; do not move server decisions into React |
| Prompts, providers, interpretation, judge | [AI](okf/ai.md), [Server](okf/server.md) | Versioned prompts and adapters in `Infrastructure`; validation and decisions in `Core` |
| API response or request shape | [Server](okf/server.md), [Frontend](okf/frontend.md) | Change the API and web client/contract tests together |
| Tests, fixtures, evaluation, release checks | [Verification](okf/verification.md) | Tests in the owning layer; eval in `tools/FindThatBook.Eval` and `eval/` |

## Rules that cross every task

- AI interprets; deterministic C# decides which real Open Library works appear, their order, and `match`/`ambiguous`/`none`. Ground explanations in validated facts. Search must work without an AI key.
- The UI displays the API's decisions in server order. Never invent confidence, progress, book facts, or provider data.
- Keep keys out of Git, logs, responses, fixtures, browser storage, and URLs. Preserve the Development, loopback, and same-origin guards on admin routes.
- Update the owning layer, its focused tests, and any affected contract, design, or OKF concept in the same change. Use [Verification](okf/verification.md) for the appropriate checks and report what actually ran.

## Keeping OKF useful

`okf/` follows [OKF v0.2](https://github.com/GoogleCloudPlatform/knowledge-catalog/blob/main/okf/SPEC.md). Keep concepts short and sourced. Every concept needs YAML frontmatter with `type`; `index.md` is the navigation file. When a durable rule changes, update the relevant concept and source link, refresh `generated.at`, and remove any verification that no longer applies. Do not mark agent work as human-verified. Record uncertain or proposed rules as `status: draft`; do not add transient run results as permanent rules.
