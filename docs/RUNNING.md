# Running Find That Book

Everything here works the same on macOS, Windows and Linux. The short version is in the [README](../README.md).

## What you need

| | Version | macOS | Windows |
|---|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0 (any 10.0.1xx or later; `global.json` rolls forward within 10.0, not to 11) | the `.pkg` installer, or `brew install --cask dotnet-sdk` | the installer, or `winget install Microsoft.DotNet.SDK.10` |
| [Node.js](https://nodejs.org/en/download) | 24 LTS recommended (`.nvmrc`). 20.19+ or 22.12+ runs the app; the unit tests need 22.22.2+, 24.15+ or 26+ | the installer, `nvm install` / `fnm use` (reads `.nvmrc`), or `brew install node@24` (then follow brew's PATH note) | the installer, `winget install OpenJS.NodeJS.LTS`, or `nvm install 24` (nvm-windows) |
| [Docker](https://docs.docker.com/get-started/get-docker/) | optional, only for the container run | Docker Desktop | Docker Desktop |

Homebrew and winget are conveniences, not requirements. Nothing is installed globally by this repo: no global npm
packages, no shell or PowerShell scripts, no HTTPS dev certificate. Linux works the same way (CI runs these commands
on Ubuntu and Windows).

## Quick start: two commands

```
npm install
npm run dev
```

Open **http://localhost:5173**. The first run restores NuGet packages and builds the API, which takes about a minute.
After that it starts in a few seconds. The web app hot-reloads as you edit. Ctrl+C stops everything. (If you skip
`npm install`, `npm run dev` runs it for you.)

Production-like, on one origin:

```
npm start
```

This builds the web app into the API's `wwwroot` and runs the API, which serves the app, its deep links and `/api`.
Open **http://localhost:5080**.

Not sure your machine is ready? `npm run doctor` checks everything and prints the exact fix for anything missing.

## Docker: one command

```
docker compose up --build
```

Open **http://localhost:8080**. Stop with Ctrl+C, then run `docker compose down`. Without Compose:
`docker build -t find-that-book .` then `docker run --rm -p 8080:8080 --env-file .env find-that-book` (leave out
`--env-file .env` if you have no keys). The container runs in Production, so the Admin page is off there (see below).

## API keys (optional)

The app works without a key: it parses queries with basic matching and explains matches with standard sentences, and
the home page says AI is off. Turn the AI on whichever way you prefer (a free Gemini key:
https://aistudio.google.com/apikey):

**In the app (nothing to edit).** With `npm run dev` or `npm start` running, open the Admin page
(http://localhost:5173/admin, or http://localhost:5080/admin for `npm start`) and paste the key under **Turn on AI**,
then **Save key**. It works straight away and lasts until you stop the app. The key goes to the API on your machine
only and is never shown again. Adding an Anthropic or OpenAI key there switches searches to that provider.

**In `.env` (kept across restarts).**

```
cp .env.example .env        # Windows: copy .env.example .env
```

Set `GEMINI_API_KEY=your-key` in `.env` and restart. For Anthropic or OpenAI, set `ANTHROPIC_API_KEY` or
`OPENAI_API_KEY` instead: the app uses the first provider that has a key (see the comments in `.env.example`).
Environment variables work too, and win over `.env`. `npm run dev` and `npm start` print whether AI is on.
`.env` is git-ignored and Docker-ignored. Compose passes it to the container as environment variables when it exists,
so keys are never built into the image. Docker has no Admin page, so `.env` is the way there.

## Admin page

`/admin` (add or replace keys in memory, switch the AI provider, test a connection, turn the second opinion on or off)
exists only in Development, and only for callers on the same machine. `npm run dev` and `npm start` both run Development, so it works at
http://localhost:5173/admin and http://localhost:5080/admin. The Docker image runs Production: there `/admin` shows
the app's page, but the `/api/admin` endpoints answer 404 by design.

## Ports and settings

| What | Default | Change it |
|---|---|---|
| API (`npm run dev`, `npm start`) | 5080 | `--api-port 5090` or `FTB_API_PORT` |
| Web dev server (`npm run dev`) | 5173 | `--web-port 5174` or `FTB_WEB_PORT` |
| Docker | 8080 | `FTB_DOCKER_PORT=8081 docker compose up --build` |
| Use an API that is already running elsewhere (`npm run dev` only) | | `FTB_API_URL=http://host:port` |

Flags go after `--`, the same in every shell: `npm run dev -- --api-port 5090 --web-port 5174`.
Environment variables, by shell:

```
FTB_API_PORT=5090 npm run dev                # macOS / Linux
$env:FTB_API_PORT=5090; npm run dev          # PowerShell
set "FTB_API_PORT=5090" && npm run dev       # Command Prompt
```

If an API already answers `/api/health` on the API port, `npm run dev` reuses it and leaves it running when you stop.

## What each command does

| Command | What it does |
|---|---|
| `npm run dev` | Checks Node, the web dependencies (installs them if missing), the .NET SDK, `.env` and the ports. Starts `dotnet run --project src/FindThatBook.Api --launch-profile http` (Development) and waits for `/api/health`. Then starts Vite with hot reload, which proxies `/api`, `/openapi` and `/scalar` to the API, and prints the URL to open. Both processes' output is shown with `[api]` / `[web]` prefixes. Ctrl+C, a closed terminal or either process exiting stops both process trees. |
| `npm start` | Runs `vite build` into `src/FindThatBook.Api/wwwroot`, then `dotnet run` (Release build, Development environment). One origin: the app, deep links like `/admin`, and `/api` (unknown `/api/*` routes are JSON 404s, never the app's HTML). |
| `npm run build` | Typechecks and builds the web app into `wwwroot`. |
| `npm test` | The web unit tests (Vitest). Backend tests: `dotnet test`. |
| `npm run check` | Web typecheck, lint, unit tests and build: what CI runs. |
| `npm run doctor` | Checks Node, web dependencies, the .NET SDK, `.env`, both ports and Docker, and says how to fix each problem. Changes nothing. |
| `npm run smoke` | Runs `npm start` and `npm run dev` for real, checks the pages, `/api/health` and a JSON 404 for an unknown `/api` route, stops them like Ctrl+C would, and checks nothing is left listening. CI runs it on Ubuntu and Windows. Never runs a search, so it costs nothing. |
| `docker compose up --build` | Builds the web app (Node 24), publishes the API (.NET 10 SDK) and runs both in one ASP.NET Core runtime image as a non-root user on port 8080, in Production. |

The scripts live in `scripts/` and use plain Node with no dependencies. Each process is started directly (no shell,
no `.cmd` shims), so paths with spaces and Windows work the same as macOS.

## Troubleshooting

Start with `npm run doctor`.

- **Port already in use.** The message names the port, a command to find the program using it (`lsof -nP -iTCP:5080 -sTCP:LISTEN`, or `netstat -ano | findstr :5080` on Windows) and the flag for another port. On Windows, Hyper-V and WSL reserve some port ranges, which fails with "access permissions" errors. List them with `netsh interface ipv4 show excludedportrange protocol=tcp` and pick a port outside them.
- **.NET SDK not found, or the wrong version.** `dotnet --list-sdks` must show a 10.0 SDK; a newer major alone (11.x) isn't used. After installing, open a new terminal so `dotnet` is on PATH.
- **Node too old.** Vite 8 needs 20.19+ or 22.12+, and the unit tests need 22.22.2+, 24.15+ or 26+. Use Node 24 LTS (`nvm install` / `fnm use` read `.nvmrc`).
- **Windows: "running scripts is disabled on this system".** This repo ships no PowerShell scripts, so its execution policy doesn't matter here. If PowerShell blocks `npm` itself (`npm.ps1`), use `npm.cmd run dev`, run from Command Prompt, or allow local scripts with `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.
- **Windows: "Terminate batch job (Y/N)?" after Ctrl+C.** That's `npm.cmd` asking. The app has already stopped, so answer Y.
- **Windows build error "being used by another process".** An API from an earlier run is still running: `taskkill /im FindThatBook.Api.exe /f` (macOS/Linux: `pkill -f FindThatBook.Api`).
- **Corporate proxy or TLS inspection.** npm reads `HTTPS_PROXY` / `npm config set https-proxy`, and NuGet reads `HTTPS_PROXY`. If Node rejects your company's certificate, set `NODE_EXTRA_CA_CERTS` to its PEM file. Docker builds use the proxy set in Docker Desktop (Settings > Resources > Proxies).
- **The first run seems stuck.** `npm run dev` prints a progress line every 20 seconds while NuGet restores and the API builds, and gives up after 5 minutes with the build output above it.
- **"AI: off".** No key was found. Put `GEMINI_API_KEY` in `.env` at the repo root (next to `FindThatBook.slnx`), then restart.
- **After `git pull`.** If doctor says `package-lock.json` changed, run `npm install`.
- **A `VBCSCompiler` process stays after Ctrl+C.** That's the C# compiler server, which .NET keeps warm for a few minutes and then exits. The API and Vite processes are always stopped.
