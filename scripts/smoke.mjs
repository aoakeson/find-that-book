// node scripts/smoke.mjs <dev|start> [--api-port N] [--web-port N]
// Runs scripts/dev.mjs or scripts/start.mjs the way a reviewer would, checks the pages and the API answers, stops it
// the way Ctrl+C would, and checks that nothing is left listening. Free: only /api/health, pages and an unknown /api
// route are requested (never a search). CI runs it on Linux and Windows.
import { fork } from "node:child_process"
import path from "node:path"
import { httpGet, isApiHealth, isPortOpen, isWindows, paint, parseCommandLine, repoRoot, say, sleep } from "./lib.mjs"

const [mode, ...rest] = process.argv.slice(2)
if (mode !== "dev" && mode !== "start") {
  say("Usage: node scripts/smoke.mjs <dev|start> [--api-port <port>] [--web-port <port>]")
  process.exit(2)
}
const { apiPort, webPort } = parseCommandLine(mode, rest, { webPort: mode === "dev" })
const base = mode === "dev" ? `http://localhost:${webPort}` : `http://localhost:${apiPort}`
const ports = mode === "dev" ? [apiPort, webPort] : [apiPort]

for (const port of ports) {
  if (await isPortOpen(port)) {
    say(paint.red(`Port ${port} is in use; the smoke test needs its ports free (pass --api-port / --web-port).`))
    process.exit(1)
  }
}

const results = []
const check = (name, ok, detail) => {
  results.push(ok)
  say(`${ok ? paint.green("pass") : paint.red("FAIL")}  ${name}${ok ? "" : `: ${detail}`}`)
}

const child = fork(path.join(repoRoot, "scripts", `${mode}.mjs`), rest, { stdio: ["ignore", "inherit", "inherit", "ipc"] })
let exited = null
const exit = new Promise((resolve) => child.once("exit", (code, signal) => resolve((exited = { code, signal }))))

async function ready() {
  const deadline = Date.now() + 6 * 60_000
  while (!exited && Date.now() < deadline) {
    try {
      const health = await httpGet(`${base}/api/health`, 1500)
      // dev: the banner prints once Vite answers; start: once the API is healthy.
      if (health.status === 200 && (await httpGet(`${base}/`, 1500)).status === 200) return true
    } catch {
      // not up yet
    }
    await sleep(1000)
  }
  return false
}

const up = await ready()
check(`npm ${mode === "dev" ? "run dev" : "start"} comes up on ${base}`, up, exited ? `exited early (${JSON.stringify(exited)})` : "timed out")

if (up) {
  await sleep(1500) // let the runner print its banner before our lines
  const health = await httpGet(`${base}/api/health`)
  let body = null
  try {
    body = JSON.parse(health.body)
  } catch {
    // reported below
  }
  check("GET /api/health is this API's JSON", health.status === 200 && isApiHealth(body), `${health.status} ${health.type}`)

  const home = await httpGet(`${base}/`)
  check("GET / is the web app's HTML", home.type.includes("text/html") && home.body.includes('id="root"'), `${home.status} ${home.type}`)
  if (mode === "dev") check("GET / comes from the Vite dev server (hot reload)", home.body.includes("/@vite/client"), "no /@vite/client script")

  for (const route of ["/admin", "/some/unknown/page"]) {
    const page = await httpGet(`${base}${route}`)
    check(`GET ${route} (deep link) is the app's HTML`, page.status === 200 && page.body.includes('id="root"'), `${page.status} ${page.type}`)
  }

  const unknown = await httpGet(`${base}/api/nope`)
  check(
    "GET /api/nope is a JSON 404, never HTML",
    unknown.status === 404 && unknown.type.includes("json") && !unknown.body.trimStart().startsWith("<"),
    `${unknown.status} ${unknown.type}`,
  )
}

// Stop it like a person would: Ctrl+C (SIGINT) on macOS/Linux. Windows can't signal one process, so ask over IPC;
// the runner then takes the same path (taskkill /T on each process tree).
if (!exited) {
  if (isWindows) child.send("stop")
  else child.kill("SIGINT")
}
const stopped = await Promise.race([exit, sleep(30_000).then(() => null)])
check("stops cleanly with exit code 0", stopped?.code === 0, stopped ? JSON.stringify(stopped) : "still running after 30s")

for (const port of ports) {
  let open = await isPortOpen(port)
  for (let i = 0; open && i < 20; i++) {
    await sleep(250)
    open = await isPortOpen(port)
  }
  check(`nothing is left listening on port ${port}`, !open, "still open")
}

if (!stopped) child.kill("SIGKILL")
const failed = results.filter((ok) => !ok).length
say(failed ? paint.red(`${failed} of ${results.length} checks failed.`) : paint.green(`All ${results.length} checks passed.`))
process.exit(failed ? 1 : 0)
