import { Link } from "@tanstack/react-router"
import { SignpostIcon } from "lucide-react"
import { buttonVariants } from "@/components/ui/button"
import { useDocumentTitle } from "@/lib/use-document-title"
import { cn } from "@/lib/utils"
import { StatusScreen } from "./StatusScreen"

export function NotFoundPage() {
  useDocumentTitle("Page not found")
  return (
    <StatusScreen
      icon={<SignpostIcon />}
      title="Page not found"
      description="There's no page at this address. Check the link, or go back to search."
      actions={
        <Link to="/" className={cn(buttonVariants({ size: "lg" }))}>
          Back to search
        </Link>
      }
    />
  )
}
