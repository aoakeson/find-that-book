// Shared by dev.mjs, start.mjs, doctor.mjs and smoke.mjs. Plain Node ESM with built-ins only. Nothing goes through a
// shell: every process is an executable plus an argument array, so paths with spaces and Windows behave like macOS.
// This file must still load on an old Node, so that the version check can explain what to install.
import { spawn, spawnSync } from "node:child_process"
import { existsSync, readFileSync, realpathSync, statSync } from "node:fs"
import net from "node:net"
import path from "node:path"
import readline from "node:readline"
import { fileURLToPath } from "node:url"

export const isWindows = process.platform === "win32"
export const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..")
export const webDir = path.join(repoRoot, "web")
export const apiProject = path.join("src", "FindThatBook.Api")
export const DEFAULT_API_PORT = 5080
export const DEFAULT_WEB_PORT = 5173

export const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

// ---------------------------------------------------------------------------------------------------------------
// Output

export const colourEnabled =
  !process.env.NO_COLOR &&
  (process.env.FORCE_COLOR ? process.env.FORCE_COLOR !== "0" : Boolean(process.stdout.isTTY) && process.env.TERM !== "dumb")
const style = (open, close) => (text) => (colourEnabled ? `\u001b[${open}m${text}\u001b[${close}m` : String(text))
export const paint = {
  bold: style(1, 22),
  dim: style(2, 22),
  red: style(31, 39),
  green: style(32, 39),
  yellow: style(33, 39),
  magenta: style(35, 39),
  cyan: style(36, 39),
}

export function say(text = "") {
  process.stdout.write(`${text}\n`)
}

const TAGS = { ok: paint.green("ok   "), info: paint.cyan("info "), warn: paint.yellow("warn "), error: paint.red("error") }

/** A check result is { level: "ok" | "info" | "warn" | "error", text, fix?: string[] }. */
export function printCheck(result) {
  say(`  ${TAGS[result.level]} ${result.text}`)
  for (const line of result.fix ?? []) say(`        ${line}`)
}

export function printBanner(title, rows) {
  const width = Math.max(...rows.map(([key]) => key.length))
  say("")
  say(`  ${paint.bold(paint.green(title))}`)
  say("")
  for (const [key, value] of rows) say(`  ${paint.dim(key.padEnd(width))}   ${value}`)
  say("")
}

// ---------------------------------------------------------------------------------------------------------------
// Command line: --api-port / --web-port (or FTB_API_PORT / FTB_WEB_PORT)

export function parseCommandLine(command, argv, { webPort: wantsWebPort = false } = {}) {
  const names = wantsWebPort ? ["api-port", "web-port"] : ["api-port"]
  const values = {}
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]
    if (arg === "--help" || arg === "-h") {
      say(usage(command, names))
      process.exit(0)
    }
    const [flag, inline] = arg.split("=", 2)
    const name = flag.replace(/^--/, "")
    if (!flag.startsWith("--") || !names.includes(name)) exitWithUsage(command, names, `Unknown option: ${arg}`)
    const value = inline ?? argv[++i]
    if (value === undefined) exitWithUsage(command, names, `${flag} needs a value`)
    values[name] = value
  }
  const apiPort = readPort(values["api-port"] ?? process.env.FTB_API_PORT, DEFAULT_API_PORT, "API port", command, names)
  const webPort = wantsWebPort
    ? readPort(values["web-port"] ?? process.env.FTB_WEB_PORT, DEFAULT_WEB_PORT, "web port", command, names)
    : DEFAULT_WEB_PORT
  if (wantsWebPort && apiPort === webPort) exitWithUsage(command, names, "The API and web ports must differ.")
  return { apiPort, webPort }
}

function readPort(raw, fallback, label, command, names) {
  const text = raw === undefined ? "" : String(raw).trim() // trim: cmd.exe's `set X=1 && …` keeps a trailing space
  if (text === "") return fallback
  const port = Number(text)
  if (!Number.isInteger(port) || port < 1 || port > 65535) exitWithUsage(command, names, `The ${label} must be a number from 1 to 65535 (got "${text}").`)
  return port
}

function usage(command, names) {
  const run = command === "start" ? "npm start" : `npm run ${command}`
  const flags = names.map((name) => `[--${name} <port>]`).join(" ")
  return [
    `Usage: ${run} -- ${flags}`,
    "",
    `  --api-port   API port (default ${DEFAULT_API_PORT}; env FTB_API_PORT)`,
    ...(names.includes("web-port") ? [`  --web-port   Vite dev server port (default ${DEFAULT_WEB_PORT}; env FTB_WEB_PORT)`] : []),
  ].join("\n")
}

function exitWithUsage(command, names, message) {
  say(paint.red(message))
  say(usage(command, names))
  process.exit(2)
}

export function findListener(port) {
  return isWindows ? `netstat -ano | findstr :${port}` : `lsof -nP -iTCP:${port} -sTCP:LISTEN`
}

/** How to get past a busy port, for the command that hit it. */
export function portHelp(command, kind, port) {
  const run = command === "start" ? "npm start" : `npm run ${command}`
  const flag = kind === "api" ? "--api-port" : "--web-port"
  const variable = kind === "api" ? "FTB_API_PORT" : "FTB_WEB_PORT"
  return [
    `Stop whatever uses it (find it with: ${findListener(port)}),`,
    `or use another port: ${run} -- ${flag} ${port + 10}   (or set ${variable})`,
  ]
}

// ---------------------------------------------------------------------------------------------------------------
// Prerequisite checks

const NODE_FIX = [
  "Install Node.js 24 LTS: https://nodejs.org/en/download",
  "(macOS: brew install node@24, then follow brew's PATH note. Windows: winget install OpenJS.NodeJS.LTS. nvm/fnm: nvm install 24)",
]
// From the installed packages' "engines": Vite 8 needs ^20.19 || >=22.12; jsdom 30 and Vitest 5 (unit tests) need
// ^22.22.2 || ^24.15 || >=26. Each range is [lowest version, first major that is no longer included].
const VITE_RANGES = [[[20, 19, 0], 21], [[22, 12, 0]]]
const TEST_RANGES = [[[22, 22, 2], 23], [[24, 15, 0], 25], [[26, 0, 0]]]

export function checkNode(version = process.versions.node) {
  const parts = version.split(".").map((part) => Number.parseInt(part, 10))
  const compare = (a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2]
  const within = (ranges) => ranges.some(([low, nextMajor]) => compare(parts, low) >= 0 && (nextMajor === undefined || parts[0] < nextMajor))
  if (!within(VITE_RANGES)) {
    return { level: "error", text: `Node.js ${version} is too old for Vite 8 (it needs 20.19+ or 22.12+)`, fix: NODE_FIX }
  }
  if (!within(TEST_RANGES)) {
    return {
      level: "warn",
      text: `Node.js ${version} runs the app, but the unit-test tools (jsdom 30, Vitest 5) support 22.22.2+, 24.15+ or 26+`,
      fix: ["For npm test and npm run check, use Node.js 24 LTS (24.15 or later)."],
    }
  }
  return { level: "ok", text: `Node.js ${version}` }
}

const DOTNET_FIX = [
  "Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0",
  "(macOS: the .pkg installer there, or brew install --cask dotnet-sdk. Windows: winget install Microsoft.DotNet.SDK.10)",
  "Then open a new terminal so the dotnet command is on PATH.",
]

/** `dotnet --version` from the repo root applies global.json (10.0.100, rolling forward to any later 10.0 SDK). */
export function checkDotnet() {
  const run = (args) =>
    spawnSync("dotnet", args, { cwd: repoRoot, encoding: "utf8", windowsHide: true, timeout: 60_000, env: dotnetEnv() })
  const version = run(["--version"])
  if (version.error) {
    const missing = version.error.code === "ENOENT"
    return { level: "error", text: missing ? ".NET SDK not found (no dotnet command on PATH)" : `Could not run dotnet: ${version.error.message}`, fix: DOTNET_FIX }
  }
  if (version.status === 0) return { level: "ok", text: `.NET SDK ${version.stdout.trim()}` }
  const installed = (run(["--list-sdks"]).stdout ?? "").split(/\r?\n/).map((line) => line.split(" ")[0]).filter(Boolean)
  return {
    level: "error",
    text: `No .NET 10 SDK found${installed.length ? ` (installed: ${installed.join(", ")})` : ""}; global.json needs 10.0.100 or a later 10.0 SDK`,
    fix: DOTNET_FIX,
  }
}

export function findPackage(name) {
  for (const base of [webDir, repoRoot]) {
    const file = path.join(base, "node_modules", name, "package.json")
    if (existsSync(file)) return file
  }
  return null
}

/** The JS entry of a package's bin, run with process.execPath: no .cmd shims, no shell. */
export function packageBin(name, binName = name) {
  const file = findPackage(name)
  if (!file) return null
  const pkg = JSON.parse(readFileSync(file, "utf8"))
  const relative = typeof pkg.bin === "string" ? pkg.bin : pkg.bin?.[binName]
  return relative ? path.join(path.dirname(file), relative) : null
}

const mtime = (file) => {
  try {
    return statSync(file).mtimeMs
  } catch {
    return null
  }
}

export function checkDependencies() {
  const vite = findPackage("vite")
  if (!vite) return { level: "error", text: "Web dependencies are not installed", fix: ["Run once, at the repo root: npm install"] }
  // npm writes node_modules/.package-lock.json on every install; a lockfile newer than that means a pull changed it.
  const lock = mtime(path.join(repoRoot, "package-lock.json"))
  const installed = mtime(path.join(repoRoot, "node_modules", ".package-lock.json"))
  if (lock !== null && installed !== null && lock - installed > 60_000) {
    return { level: "warn", text: "package-lock.json changed after the last install", fix: ["Run: npm install"] }
  }
  return { level: "ok", text: `Web dependencies installed (Vite ${JSON.parse(readFileSync(vite, "utf8")).version})` }
}

/** npm's own JS entry, so the install runs without npm.cmd and without a shell. */
function findNpmCli() {
  const besideNode = path.dirname(process.execPath)
  const candidates = [
    process.env.npm_execpath, // set when started through npm run
    path.join(besideNode, "node_modules", "npm", "bin", "npm-cli.js"), // Windows installer, nvm-windows
    path.join(besideNode, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js"), // nvm, fnm, official .pkg/tarball
    ...(isWindows ? [] : (process.env.PATH ?? "").split(path.delimiter).map((dir) => realpathOrNull(path.join(dir, "npm")))), // Homebrew
  ]
  return candidates.find((file) => file && /npm-cli\.js$/.test(file) && existsSync(file)) ?? null
}

function realpathOrNull(file) {
  try {
    return realpathSync(file)
  } catch {
    return null
  }
}

/** dev/start: install the web dependencies once if node_modules is missing. False when they are still missing. */
export function ensureDependencies(log) {
  const status = checkDependencies()
  if (status.level === "warn") printCheck(status)
  if (status.level !== "error") return true
  const npmCli = findNpmCli()
  if (!npmCli) {
    printCheck(status)
    return false
  }
  log("Installing the web dependencies (first run only): npm install")
  const install = spawnSync(process.execPath, [npmCli, "install", "--no-audit", "--no-fund"], { cwd: repoRoot, stdio: "inherit" })
  if (install.status === 0 && findPackage("vite")) return true
  printCheck({ level: "error", text: "npm install failed (see its output above)", fix: ["Fix the problem it reports, then run: npm install"] })
  return false
}

const KEY_VARIABLES = ["GEMINI_API_KEY", "GOOGLE_API_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "AI__Gemini__ApiKey", "AI__Anthropic__ApiKey", "AI__OpenAI__ApiKey"]

/** Looks only at whether .env exists and which key names are set; never reads or prints a value. */
export function checkEnvFile() {
  if (existsSync(path.join(repoRoot, ".env"))) return { level: "ok", text: ".env found (the API reads its keys from it)" }
  const fromEnvironment = KEY_VARIABLES.filter((name) => process.env[name]?.trim())
  if (fromEnvironment.length > 0) return { level: "ok", text: `No .env; the API will use ${fromEnvironment.join(", ")} from the environment` }
  return {
    level: "warn",
    text: "No .env file, so AI starts off (search still works, with basic matching)",
    fix: [
      "To turn AI on, either paste a key on the Admin page once the app runs (lasts until it stops),",
      "or keep one in .env: copy .env.example to .env, set GEMINI_API_KEY, then restart:",
      isWindows ? "copy .env.example .env" : "cp .env.example .env",
    ],
  }
}

export function checkDocker() {
  const docker = spawnSync("docker", ["--version"], { encoding: "utf8", windowsHide: true, timeout: 15_000 })
  if (docker.status === 0) {
    const version = docker.stdout.match(/version ([^\s,]+)/)?.[1] ?? "found"
    return { level: "info", text: `Docker ${version} (optional: docker compose up --build runs everything in one container)` }
  }
  return { level: "info", text: "Docker not found (optional; only needed for the container run)" }
}

// ---------------------------------------------------------------------------------------------------------------
// Ports and HTTP

function connects(host, port) {
  return new Promise((resolve) => {
    const socket = net.connect({ host, port })
    const done = (open) => {
      socket.destroy()
      resolve(open)
    }
    socket.setTimeout(1000, () => done(false))
    socket.once("connect", () => done(true))
    socket.once("error", () => done(false))
  })
}

/** True when something accepts connections on localhost:port, over IPv4 or IPv6 (Vite may listen on ::1 only). */
export async function isPortOpen(port) {
  const [v4, v6] = await Promise.all([connects("127.0.0.1", port), connects("::1", port)])
  return v4 || v6
}

/**
 * "localhost" can resolve to ::1 for one program and 127.0.0.1 for another (seen in Linux containers: Vite listened on
 * one, fetch tried the other), so a localhost request tries both loopback addresses before giving up.
 */
export async function httpGet(url, timeoutMs = 5000) {
  const parsed = new URL(url)
  const hosts = parsed.hostname === "localhost" ? ["127.0.0.1", "[::1]"] : [parsed.hostname]
  let lastError
  for (const host of hosts) {
    parsed.hostname = host
    try {
      const response = await fetch(parsed, { signal: AbortSignal.timeout(timeoutMs), redirect: "manual" })
      return { status: response.status, type: response.headers.get("content-type") ?? "", body: await response.text() }
    } catch (error) {
      lastError = error
    }
  }
  throw lastError
}

/** This API's /api/health shape (not just any server that answers on the port). */
export function isApiHealth(body) {
  return Boolean(body) && body.status === "ok" && typeof body.environment === "string" && "ai_configured" in body && "suggest_index" in body
}

export async function probeHealth(baseUrl, timeoutMs = 2000) {
  try {
    const response = await httpGet(`${baseUrl}/api/health`, timeoutMs)
    const body = response.status === 200 ? JSON.parse(response.body) : null
    return isApiHealth(body) ? body : null
  } catch {
    return null
  }
}

/** adminUrl: where a key can be pasted (Development only), or null. */
export function describeAi(health, adminUrl) {
  if (health.ai_configured) return `on (${health.ai_provider})`
  const ways = adminUrl ? `paste a key at ${adminUrl}, or set GEMINI_API_KEY in .env and restart` : "set GEMINI_API_KEY in .env and restart"
  return `off, so searches use basic matching. To turn it on: ${ways}`
}

// ---------------------------------------------------------------------------------------------------------------
// The API process

export function dotnetEnv() {
  // No first-run banner; no MSBuild worker nodes left running after the build (they would outlive Ctrl+C).
  return { ...process.env, DOTNET_NOLOGO: "1", MSBUILDDISABLENODEREUSE: process.env.MSBUILDDISABLENODEREUSE ?? "1" }
}

export function dotnetRunArgs({ port, configuration }) {
  const args = ["run", "--project", apiProject, "--launch-profile", "http"]
  if (configuration) args.push("--configuration", configuration)
  if (port !== DEFAULT_API_PORT) args.push("--", "--urls", `http://localhost:${port}`)
  return args
}

// ---------------------------------------------------------------------------------------------------------------
// Process supervision: prefixed output, and on Ctrl+C, SIGTERM, a terminal closing or any child exiting, every
// process tree goes away. POSIX: each child leads its own process group, and the group is signalled (dotnet run's
// app is a grandchild). Windows: `taskkill /T /F` ends the tree (detached would open new console windows there).

const CTRL_C_EXIT_CODES = new Set([0xc000013a, -1073741510]) // Windows STATUS_CONTROL_C_EXIT

function groupAlive(pid) {
  try {
    process.kill(-pid, 0)
    return true
  } catch (error) {
    return error.code === "EPERM"
  }
}

function signalGroup(pid, signal) {
  try {
    process.kill(-pid, signal)
  } catch {
    // already gone
  }
}

async function groupGone(pid, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (groupAlive(pid) && Date.now() < deadline) await sleep(100)
  return !groupAlive(pid)
}

function taskkill(pid) {
  spawnSync("taskkill", ["/pid", String(pid), "/T", "/F"], { stdio: "ignore", windowsHide: true })
}

function forwardLines(stream, target, prefix) {
  readline.createInterface({ input: stream, crlfDelay: Infinity }).on("line", (line) => target.write(`${prefix} ${line}\n`))
}

export function createRunner(label) {
  const procs = []
  let stopping = null
  let exitCode = 0
  let firstSignalAt = 0
  const log = (text) => say(`${paint.bold(`[${label}]`)} ${text}`)

  /**
   * Starts a supervised process. A process that exits on its own stops everything, unless it is `oneShot` (a build
   * step the caller awaits through `proc.exit`). Set `proc.ready = true` once it is serving.
   */
  function start(name, command, args, { cwd = repoRoot, env = process.env, colour = paint.cyan, oneShot = false, port } = {}) {
    const child = spawn(command, args, { cwd, env, stdio: ["pipe", "pipe", "pipe"], detached: !isWindows, windowsHide: true })
    const proc = { name, pid: child.pid, port, ready: false, done: false, code: null, signal: null }
    proc.exit = new Promise((resolve) => {
      const finish = (code, signal) => {
        if (proc.done) return
        Object.assign(proc, { done: true, code, signal })
        resolve(proc)
      }
      child.once("error", (error) => {
        log(paint.red(`Could not start ${command}: ${error.message}`))
        finish(1, null)
      })
      child.once("exit", finish)
    })
    // stdin stays open and unused: Vite stops when its stdin ends, and nothing here should read the terminal.
    child.stdin.on("error", () => {})
    const prefix = colour(`[${name}]`)
    forwardLines(child.stdout, process.stdout, prefix)
    forwardLines(child.stderr, process.stderr, prefix)
    procs.push(proc)
    if (!oneShot) proc.exit.then(() => exitedOnItsOwn(proc))
    return proc
  }

  function exitedOnItsOwn(proc) {
    if (stopping) return
    // On Windows, Ctrl+C reaches the children too, and one may exit before this process sees its own SIGINT.
    setTimeout(() => {
      if (stopping) return
      if (proc.signal === "SIGINT" || CTRL_C_EXIT_CODES.has(proc.code)) return stop(0)
      const how = proc.signal ? `signal ${proc.signal}` : `exit code ${proc.code}`
      log(
        paint.red(
          proc.ready
            ? `The ${proc.name} process stopped (${how}); stopping the rest.`
            : `The ${proc.name} process stopped before it was ready (${how}). The reason is in its [${proc.name}] output above.`,
        ),
      )
      stop(proc.code || 1)
    }, 500)
  }

  async function terminate(proc) {
    if (!proc.pid) return
    if (isWindows) {
      if (!proc.done) taskkill(proc.pid)
      await Promise.race([proc.exit, sleep(5000)])
      return
    }
    if (!groupAlive(proc.pid)) return
    signalGroup(proc.pid, "SIGTERM")
    if (await groupGone(proc.pid, 10_000)) return
    log(paint.yellow(`The ${proc.name} process did not stop within 10s; killing it.`))
    signalGroup(proc.pid, "SIGKILL")
    await groupGone(proc.pid, 2000)
  }

  function killNow(proc) {
    if (!proc.pid) return
    if (isWindows) {
      if (!proc.done) taskkill(proc.pid)
    } else if (groupAlive(proc.pid)) {
      signalGroup(proc.pid, "SIGKILL")
    }
  }

  async function reportBusyPorts() {
    for (const proc of procs.filter((p) => p.port)) {
      const deadline = Date.now() + 3000
      let open = await isPortOpen(proc.port)
      while (open && Date.now() < deadline) {
        await sleep(150)
        open = await isPortOpen(proc.port)
      }
      if (open) log(paint.yellow(`Something still listens on port ${proc.port}. Find it with: ${findListener(proc.port)}`))
    }
  }

  function stop(code = 0) {
    if (stopping) return stopping
    exitCode = code
    stopping = (async () => {
      await Promise.all(procs.map(terminate))
      await reportBusyPorts()
      log(exitCode === 0 ? "Stopped." : `Stopped (exit code ${exitCode}).`)
      process.exit(exitCode)
    })()
    return stopping
  }

  function onSignal(source) {
    if (stopping) {
      // npm forwards the terminal's Ctrl+C, so one key press can arrive twice; only a later press forces.
      if (source !== "ipc" && Date.now() - firstSignalAt > 1500) {
        log("Forcing stop.")
        procs.forEach(killNow)
        process.exit(exitCode || 130)
      }
      return
    }
    firstSignalAt = Date.now()
    say("")
    log("Stopping...")
    stop(0)
  }

  for (const signal of ["SIGINT", "SIGTERM", "SIGHUP", "SIGBREAK"]) process.on(signal, () => onSignal(signal))
  // smoke.mjs (and CI on Windows, where a signal can't be sent to one process) asks for a clean stop over IPC.
  if (process.send) {
    process.on("message", (message) => message === "stop" && onSignal("ipc"))
    process.on("disconnect", () => onSignal("ipc"))
  }
  process.on("exit", () => procs.forEach(killNow))

  return {
    log,
    start,
    stop,
    fail(message, code = 1) {
      log(paint.red(message))
      return stop(code)
    },
    get stopping() {
      return stopping !== null
    },
  }
}

/** Polls /api/health until this API answers. Null when the process died, the runner stopped, or time ran out. */
export async function waitForHealth(runner, proc, baseUrl, timeoutMs = 300_000) {
  const started = Date.now()
  let nextNote = 20_000
  while (!runner.stopping && !proc.done) {
    const health = await probeHealth(baseUrl, 1500)
    if (health) {
      proc.ready = true
      return health
    }
    const elapsed = Date.now() - started
    if (elapsed > timeoutMs) {
      runner.fail(`The API did not answer ${baseUrl}/api/health within ${timeoutMs / 1000}s. Its output is above.`)
      return null
    }
    if (elapsed > nextNote) {
      runner.log(`Still waiting for the API (${Math.round(elapsed / 1000)}s). The first run restores NuGet packages and builds.`)
      nextNote += 20_000
    }
    await sleep(500)
  }
  return null
}

/** Polls a page until it answers 200. False when the process died, the runner stopped, or time ran out. */
export async function waitForPage(runner, proc, url, timeoutMs = 60_000) {
  const started = Date.now()
  while (!runner.stopping && !proc.done) {
    try {
      if ((await httpGet(url, 1500)).status === 200) {
        proc.ready = true
        return true
      }
    } catch {
      // not listening yet
    }
    if (Date.now() - started > timeoutMs) {
      runner.fail(`${url} did not answer within ${timeoutMs / 1000}s. The [${proc.name}] output above says why.`)
      return false
    }
    await sleep(300)
  }
  return false
}
