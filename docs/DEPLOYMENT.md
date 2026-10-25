# Fly.io deployment

The production app is `find-that-book` in Fly.io's `ewr` region. The public address is
https://books.andrewoakeson.com/; Fly also serves https://find-that-book.fly.dev/.
The existing `Dockerfile` builds the React app and .NET API into one image on port 8080.

## Automatic release

The `ci` GitHub Actions workflow runs on pull requests and pushes to `main`. A push to
`main` deploys only after the .NET build and tests, secret scan, web checks, browser
tests, and cross-platform startup smoke checks succeed. A local commit or a push to a
feature branch does not deploy. The deployment uses the committed `fly.toml`, builds
remotely on Fly.io, and checks `/api/health` through the custom domain afterward.

GitHub Actions holds an app-scoped Fly deploy token in the repository secret
`FLY_API_TOKEN`. Rotate that token before it expires (October 2027). AI provider keys
and `AI__Provider` stay in Fly secrets; they are not copied into GitHub Actions or Git.

To inspect a release, use the `ci` run for its commit in GitHub Actions, then check:

```text
fly status -a find-that-book
fly checks list -a find-that-book
https://books.andrewoakeson.com/api/health
```

For a manual release of an already reviewed commit, run `fly deploy --remote-only
--app find-that-book --config fly.toml` from a checkout of that exact commit. Record
that manual release separately from the GitHub Actions pipeline.

The Fly service check calls `/api/health`, which reports process liveness and
configuration without making paid AI or Open Library calls. A healthy response does
not prove either upstream is available; verify a search separately when diagnosing
search failures.
