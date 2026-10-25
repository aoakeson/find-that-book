import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { App } from "@/app/App"
import { router } from "@/router"
import "./index.css"

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App router={router} />
  </StrictMode>
)
