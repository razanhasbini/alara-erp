import type { ReactNode } from "react"
import { AlertTriangle, RefreshCw } from "lucide-react"
import { isRouteErrorResponse, Link, useRouteError } from "react-router"
import { Button } from "@/components/ui/button"

function Message({ code, title, children }: { code: string; title: string; children: ReactNode }) {
  return (
    <div className="mx-auto max-w-md py-24 text-center">
      <p className="eyebrow">{code}</p>
      <h1 className="font-display mt-2 text-3xl">{title}</h1>
      <p className="mt-3 text-sm text-muted-foreground">{children}</p>
      <Button asChild variant="outline" className="mt-6"><Link to="/">Back to dashboard</Link></Button>
    </div>
  )
}

export const UnauthorizedPage = () => (
  <Message code="403" title="Not available to your role">
    Your account does not have permission to open this page. If you need it for your work, ask an administrator.
  </Message>
)

export const NotFoundPage = () => (
  <Message code="404" title="Page not found">The page you were looking for does not exist or was moved.</Message>
)

export function RouteErrorPage() {
  const error = useRouteError()
  const detail = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : "An unexpected error occurred."

  return (
    <main className="grid min-h-svh place-items-center px-6 py-16">
      <div role="alert" className="w-full max-w-lg rounded-xl border bg-card p-6 shadow-sm">
        <div className="flex items-start gap-3">
          <div className="grid size-9 shrink-0 place-items-center rounded-full bg-tone-red-bg text-tone-red">
            <AlertTriangle className="size-4" aria-hidden />
          </div>
          <div>
            <p className="eyebrow">Page unavailable</p>
            <h1 className="font-display mt-1 text-2xl">This page could not be loaded</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              The app may have been updated or briefly disconnected. Reload the page to try again.
            </p>
          </div>
        </div>

        <div className="mt-6 flex flex-wrap gap-2">
          <Button onClick={() => window.location.reload()}>
            <RefreshCw aria-hidden />
            Reload page
          </Button>
          <Button asChild variant="outline"><Link to="/">Back to dashboard</Link></Button>
        </div>

        {import.meta.env.DEV && (
          <details className="mt-6 text-xs text-muted-foreground">
            <summary className="cursor-pointer font-medium text-foreground">Technical details</summary>
            <pre className="mt-2 overflow-x-auto whitespace-pre-wrap rounded-md bg-muted p-3 font-mono">{detail}</pre>
          </details>
        )}
      </div>
    </main>
  )
}
