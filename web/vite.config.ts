/// <reference types="vitest/config" />
import type { IncomingMessage } from "node:http"
import { resolve } from "node:path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

// The API serves this build from its wwwroot, so the app and /api share one origin in every setup.
const apiTarget = process.env.FTB_API_URL ?? "http://localhost:5080"

/**
 * Every proxied request reaches the API from loopback, so the proxy repeats the API's admin rule itself: only a
 * browser on this machine (e.g. not via `vite --host`), and only from this page's own origin (no cross-site POST to
 * the paid probe). Anything else gets a 404, like the API.
 */
function guardAdmin(req: IncomingMessage) {
  let path: string
  try {
    path = decodeURIComponent((req.url ?? "").split("?")[0]).toLowerCase()
  } catch {
    return false // malformed encoding: never forward
  }
  // The API matches decoded paths case-insensitively, so /api/ADMIN or /api/%61dmin must be guarded too.
  if (!path.startsWith("/api/admin")) return undefined
  const local = ["127.0.0.1", "::1", "::ffff:127.0.0.1"].includes(req.socket.remoteAddress ?? "")
  const origin = req.headers.origin
  const sameOrigin = origin === undefined || origin === `http://${req.headers.host}`
  return local && sameOrigin ? undefined : false
}

const apiProxy = { target: apiTarget, changeOrigin: false, bypass: guardAdmin }

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": resolve(import.meta.dirname, "./src"),
    },
  },
  server: {
    port: 5173,
    // No X-Forwarded-* headers (xfwd is off by default): the API's Development admin endpoints only answer direct
    // loopback callers, and this proxy connects from loopback.
    proxy: {
      "/api": apiProxy,
      "/openapi": { target: apiTarget, changeOrigin: false },
      "/scalar": { target: apiTarget, changeOrigin: false },
    },
  },
  preview: {
    port: 4173,
    proxy: {
      "/api": apiProxy,
    },
  },
  build: {
    outDir: resolve(import.meta.dirname, "../src/FindThatBook.Api/wwwroot"),
    emptyOutDir: true,
    rolldownOptions: {
      output: {
        // Libraries change far less often than the app: separate chunks stay cached across deploys.
        codeSplitting: {
          groups: [
            { name: "react", test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/, priority: 30 },
            { name: "ui", test: /node_modules[\\/](@base-ui|@floating-ui)[\\/]/, priority: 20 },
            { name: "vendor", test: /node_modules[\\/]/, priority: 10 },
          ],
        },
      },
    },
  },
  test: {
    // Fixtures list the API's example files with import.meta.glob; a cached transform would miss newly added ones.
    fsModuleCache: false,
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    restoreMocks: true,
    css: false,
  },
})
