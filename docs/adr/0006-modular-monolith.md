# ADR 0006: Three-project modular monolith

Status: accepted (2026-10-24)

## Context
One small API (and later one SPA) maintained by one developer. The brief grades architecture and judgement; over-built
designs (CQRS, buses, microservices) read as poor judgement.

## Decision
`FindThatBook.Core` (pure: normalization, matching, ports, orchestration; no package references), `FindThatBook.Infrastructure`
(Open Library, AI providers, judge, caching, suggest index, composition root shared with the eval tool), `FindThatBook.Api`
(minimal API, validation, ProblemDetails, OpenAPI/Scalar, rate limits). The compiler keeps I/O out of the matching logic.
No MediatR, repositories, CQRS, message bus or Aspire.

## Consequences
The React app is served from the API's `wwwroot` (one deployable: the Docker image that runs the live demo). Repository workflow: GitHub Flow (`main` always
deployable, short feature branches, CI on every pull request).
