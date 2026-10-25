// npm run dev: the API (dotnet run, Development) and the Vite dev server with hot reload, in one terminal.
// Ports: --api-port / --web-port, or FTB_API_PORT / FTB_WEB_PORT. An API already answering on the API port is reused.
// FTB_API_URL points the web app at an API running somewhere else (then no dotnet process is started).
import {
  checkEnvFile,
  checkNode,
  colourEnabled,
  createRunner,
  describeAi,
  dotnetEnv,
  dotnetRunArgs,
  ensureDependencies,
  ensureDotnet,
  restoreDotnet,
  isPortOpen,
  packageBin,
  paint,
  parseCommandLine,
  portHelp,
  printBanner,
  printCheck,
  probeHealth,
  waitForHealth,
  waitForPage,
  webDir,
} from "./lib.mjs"

const { apiPort, webPort } = parseCommandLine("dev", process.argv.slice(2), { webPort: true })

function failEarly(result) {
  printCheck(result)
  process.exit(1)
}

async function main() {
  const node = checkNode()
  if (node.level === "error") failEarly(node)
  if (node.level === "warn") printCheck(node)

  const runner = createRunner("dev")
  if (!ensureDependencies(runner.log)) process.exit(1)
  const env = checkEnvFile()
  if (env.level !== "ok") printCheck(env)

  const external = process.env.FTB_API_URL?.trim().replace(/\/+$/, "")
  const apiUrl = external || `http://localhost:${apiPort}`
  const webUrl = `http://localhost:${webPort}`

  if (await isPortOpen(webPort)) {
    failEarly({ level: "error", text: `Port ${webPort} (the web dev server) is already in use`, fix: portHelp("dev", "web", webPort) })
  }

  let health = await probeHealth(apiUrl)
  if (health) {
    runner.log(`Using the API already running at ${apiUrl} (${health.environment}). It keeps running after you stop this.`)
  } else if (external) {
    failEarly({
      level: "error",
      text: `FTB_API_URL is ${external}, but nothing there answers /api/health like this API`,
      fix: ["Start that API, or unset FTB_API_URL so npm run dev starts one."],
    })
  } else {
    if (await isPortOpen(apiPort)) {
      failEarly({ level: "error", text: `Port ${apiPort} is in use by another program (not this API)`, fix: portHelp("dev", "api", apiPort) })
    }
    if (!(await ensureDotnet(runner.log))) process.exit(1)
    if (!restoreDotnet(runner.log)) process.exit(1)
    runner.log(`Starting the API on ${apiUrl} (dotnet run, Development). The first run builds; allow a minute.`)
    const api = runner.start("api", "dotnet", dotnetRunArgs({ port: apiPort }), {
      env: dotnetEnv(),
      colour: paint.cyan,
      port: apiPort,
    })
    health = await waitForHealth(runner, api, apiUrl)
    if (!health) return
  }

  runner.log(`Starting the web dev server on ${webUrl} (Vite, hot reload; /api goes to ${apiUrl}).`)
  const web = runner.start("web", process.execPath, [packageBin("vite"), "--port", String(webPort), "--strictPort"], {
    cwd: webDir,
    env: { ...process.env, FTB_API_URL: apiUrl, ...(colourEnabled ? { FORCE_COLOR: "1" } : {}) },
    colour: paint.magenta,
    port: webPort,
  })
  if (!(await waitForPage(runner, web, webUrl))) return

  printBanner("Find That Book is running", [
    ["Open", paint.bold(paint.cyan(webUrl))],
    ["Admin", health.environment === "Development" ? `${webUrl}/admin` : `hidden (the API runs in ${health.environment})`],
    ["API", `${apiUrl}   (reference: ${apiUrl}/scalar)`],
    ["AI", describeAi(health, health.environment === "Development" ? `${webUrl}/admin` : null)],
    ["Stop", "Ctrl+C"],
  ])
}

await main()
