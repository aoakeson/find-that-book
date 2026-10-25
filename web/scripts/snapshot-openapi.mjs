// Saves the running API's OpenAPI document as src/api/openapi.snapshot.json, which contract.test.ts checks
// src/api/types.ts against. Run it after any backend contract change: `node scripts/snapshot-openapi.mjs`.
// Free to call: it only reads /openapi/v1.json. Set FTB_API_URL for an API that isn't on http://localhost:5080.
import { writeFile } from "node:fs/promises"
import { resolve } from "node:path"

const base = (process.env.FTB_API_URL ?? "http://localhost:5080").replace(/\/+$/, "")
const source = `${base}/openapi/v1.json`
const target = resolve(import.meta.dirname, "../src/api/openapi.snapshot.json")

let response
try {
  response = await fetch(source, { headers: { Accept: "application/json" }, signal: AbortSignal.timeout(15_000) })
} catch (error) {
  console.error(`Could not reach ${source}: ${error instanceof Error ? error.message : String(error)}`)
  console.error("Start the API (dotnet run --project src/FindThatBook.Api) or set FTB_API_URL.")
  process.exit(1)
}
if (!response.ok) {
  console.error(`${source} answered ${response.status}.`)
  process.exit(1)
}

const document = await response.json()
if (typeof document?.openapi !== "string" || typeof document?.components?.schemas !== "object") {
  console.error(`${source} is not an OpenAPI document.`)
  process.exit(1)
}

await writeFile(target, `${JSON.stringify(document, null, 2)}\n`)
console.log(`Saved ${Object.keys(document.components.schemas).length} schemas from ${source} to ${target}`)
