// npm start: production-like. Builds the web app into src/FindThatBook.Api/wwwroot, then runs the API (Release build,
// Development environment so the local-only Admin page works), which serves the app and /api on one origin.
// Port: --api-port, or FTB_API_PORT.
import {
  checkDotnet,
  checkEnvFile,
  checkNode,
  colourEnabled,
  createRunner,
  describeAi,
  dotnetEnv,
  dotnetRunArgs,
  ensureDependencies,
  httpGet,
  isPortOpen,
  packageBin,
  paint,
  parseCommandLine,
  portHelp,
  printBanner,
  printCheck,
  probeHealth,
  waitForHealth,
  webDir,
} from "./lib.mjs"

const { apiPort } = parseCommandLine("start", process.argv.slice(2))

function failEarly(result) {
  printCheck(result)
  process.exit(1)
}

async function main() {
  const node = checkNode()
  if (node.level === "error") failEarly(node)
  if (node.level === "warn") printCheck(node)

  const runner = createRunner("start")
  if (!ensureDependencies(runner.log)) process.exit(1)
  const dotnet = checkDotnet()
  if (dotnet.level === "error") failEarly(dotnet)
  const env = checkEnvFile()
  if (env.level !== "ok") printCheck(env)

  const url = `http://localhost:${apiPort}`
  if (await isPortOpen(apiPort)) {
    const running = await probeHealth(url)
    failEarly({
      level: "error",
      text: running
        ? `Port ${apiPort} is in use by a running Find That Book API or dev server (npm start runs its own, with a fresh build)`
        : `Port ${apiPort} is in use by another program`,
      fix: portHelp("start", "api", apiPort),
    })
  }

  runner.log("Building the web app into src/FindThatBook.Api/wwwroot (vite build)...")
  const build = runner.start("web", process.execPath, [packageBin("vite"), "build"], {
    cwd: webDir,
    env: { ...process.env, ...(colourEnabled ? { FORCE_COLOR: "1" } : {}) },
    colour: paint.magenta,
    oneShot: true,
  })
  const { code } = await build.exit
  if (runner.stopping) return
  if (code !== 0) {
    runner.fail(`The web build failed (exit code ${code}). Its output is above.`, code || 1)
    return
  }

  runner.log(`Starting the API on ${url} (dotnet run, Release build, Development environment)...`)
  const api = runner.start("api", "dotnet", dotnetRunArgs({ port: apiPort, configuration: "Release" }), {
    env: dotnetEnv(),
    colour: paint.cyan,
    port: apiPort,
  })
  const health = await waitForHealth(runner, api, url)
  if (!health) return

  const home = await httpGet(`${url}/`).catch(() => null)
  if (!home?.type.includes("text/html")) {
    runner.log(paint.yellow(`${url}/ did not return the web app (status ${home?.status ?? "none"}); the API found no wwwroot/index.html at startup.`))
  }

  printBanner("Find That Book is running (production build)", [
    ["Open", paint.bold(paint.cyan(url))],
    ["Admin", `${url}/admin   (Development only; a Production run, like the Docker image, hides it)`],
    ["API", `same origin, under /api   (reference: ${url}/scalar)`],
    ["AI", describeAi(health, health.environment === "Development" ? `${url}/admin` : null)],
    ["Stop", "Ctrl+C"],
  ])
}

await main()
