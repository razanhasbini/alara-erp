import { Link } from "react-router"
import { Button } from "@/components/ui/button"

function Message({ code, title, children }: { code: string; title: string; children: React.ReactNode }) {
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
