// npm run doctor: checks everything npm run dev and npm start need, and says exactly how to fix what is missing.
// Changes nothing. Exit code 1 when something blocks running the app.
import os from "node:os"
import {
  checkDependencies,
  checkDocker,
  checkDotnet,
  checkEnvFile,
  checkNode,
  findListener,
  isPortOpen,
  paint,
  parseCommandLine,
  portHelp,
  printCheck,
  probeHealth,
  repoRoot,
  say,
} from "./lib.mjs"

const { apiPort, webPort } = parseCommandLine("doctor", process.argv.slice(2), { webPort: true })

async function checkApiPort() {
  if (!(await isPortOpen(apiPort))) return { level: "ok", text: `Port ${apiPort} is free (API)` }
  const health = await probeHealth(`http://localhost:${apiPort}`)
  if (health) {
    return {
      level: "info",
      text: `Port ${apiPort}: this API is already running (${health.environment}). npm run dev reuses it; npm start needs the port free.`,
    }
  }
  return { level: "error", text: `Port ${apiPort} (API) is in use by another program`, fix: portHelp("dev", "api", apiPort) }
}

async function checkWebPort() {
  if (!(await isPortOpen(webPort))) return { level: "ok", text: `Port ${webPort} is free (web dev server)` }
  return {
    level: "warn",
    text: `Port ${webPort} (web dev server) is in use, so npm run dev would stop here; npm start doesn't need it`,
    fix: [`Find it with: ${findListener(webPort)}`, `or use another port: npm run dev -- --web-port ${webPort + 1}   (or set FTB_WEB_PORT)`],
  }
}

say(paint.bold("Find That Book: checking this machine"))
say(paint.dim(`  ${os.type()} ${os.release()} ${process.arch}, repo at ${repoRoot}`))
say("")

const results = [checkNode()]
printCheck(results[0])
if (typeof fetch !== "function") {
  say("")
  say(paint.red("Install a supported Node.js first, then run npm run doctor again."))
  process.exit(1)
}
for (const check of [checkDependencies, checkDotnet, checkEnvFile, checkApiPort, checkWebPort, checkDocker]) {
  const result = await check()
  results.push(result)
  printCheck(result)
}

say("")
if (results.some((result) => result.level === "error")) {
  say(paint.red("Fix the errors above, then run npm run doctor again."))
  process.exit(1)
}
say(paint.green(`Ready. Run npm run dev (hot reload) or npm start (production build).`))
