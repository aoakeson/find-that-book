// npm run setup: print the machine status, install whatever is missing (web packages, the .NET 10 SDK, NuGet packages), then stop.
// Same command on macOS, Windows and Linux. npm run dev does this itself, so setup is only for installing ahead of time.
import { checkEnvFile, checkNode, ensureDependencies, ensureDotnet, paint, printCheck, restoreDotnet, say } from "./lib.mjs"

say(paint.bold("Find That Book: checking this machine"))
say("")

const node = checkNode()
printCheck(node)
if (node.level === "error") process.exit(1)

if (!ensureDependencies(say)) process.exit(1)
printCheck({ level: "ok", text: "Web dependencies installed" })

if (!(await ensureDotnet(say))) process.exit(1)
if (!restoreDotnet(say)) process.exit(1)

const env = checkEnvFile()
if (env.level !== "ok") printCheck(env)

say("")
say(paint.green("Ready. Run npm run dev and open http://localhost:5173"))
