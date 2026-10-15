import type { ReactNode } from "react"
import { AlertTriangle, Loader2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { errorMessage } from "@/lib/api"
import { cn } from "@/lib/utils"

export function FullPageSpinner() {
  return (
    <div className="grid min-h-svh place-items-center">
      <Loader2 className="size-5 animate-spin text-muted-foreground" aria-label="Loading" />
    </div>
  )
}

export function EmptyState({ title, children, action, className }: { title: string; children?: ReactNode; action?: ReactNode; className?: string }) {
  return (
    <div className={cn("flex flex-col items-center justify-center gap-2 px-6 py-14 text-center", className)}>
      <p className="font-display text-xl">{title}</p>
      {children && <div className="max-w-sm text-sm text-muted-foreground">{children}</div>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return (
    <div role="alert" className="flex items-start gap-3 rounded-md border border-tone-red/20 bg-tone-red-bg px-4 py-3 text-sm text-tone-red">
      <AlertTriangle className="mt-0.5 size-4 shrink-0" />
      <div className="flex-1">{errorMessage(error)}</div>
      {onRetry && <Button size="xs" variant="outline" onClick={onRetry}>Retry</Button>}
    </div>
  )
}

export function InlineSpinner({ className }: { className?: string }) {
  return <Loader2 className={cn("size-4 animate-spin", className)} aria-hidden />
}
