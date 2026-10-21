import type { ReactNode } from "react"
import { Link } from "react-router"
import { ChevronLeft } from "lucide-react"
import { cn } from "@/lib/utils"

export function PageHeader({ title, description, actions, back, meta }: {
  title: ReactNode
  description?: ReactNode
  actions?: ReactNode
  back?: { to: string; label: string }
  meta?: ReactNode
}) {
  return (
    <header className="relative mb-6 flex flex-col gap-3 border-b border-hairline pb-5 after:absolute after:-bottom-px after:left-0 after:h-px after:w-16 after:bg-gold md:flex-row md:items-end md:justify-between">
      <div className="min-w-0">
        {back && (
          <Link to={back.to} className="mb-2 inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground">
            <ChevronLeft className="size-3.5" /> {back.label}
          </Link>
        )}
        <h1 className="font-display text-[1.9rem] leading-tight font-medium">{title}</h1>
        {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
        {meta && <div className="mt-2 flex flex-wrap items-center gap-2">{meta}</div>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
    </header>
  )
}

/** A white sheet section with an optional title row; used sparingly instead of card grids. */
export function Section({ title, actions, children, className, bodyClassName }: {
  title?: ReactNode
  actions?: ReactNode
  children: ReactNode
  className?: string
  bodyClassName?: string
}) {
  return (
    <section className={cn("frame rounded-lg bg-card", className)}>
      {(title || actions) && (
        <div className="flex items-center justify-between gap-3 border-b border-hairline px-4 py-2.5">
          {title && <h2 className="min-w-0 text-sm font-semibold">{title}</h2>}
          {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </div>
      )}
      <div className={bodyClassName}>{children}</div>
    </section>
  )
}

/** Label/value pairs in a definition list. */
export function Facts({ items, className }: { items: [ReactNode, ReactNode][]; className?: string }) {
  return (
    <dl className={cn("grid grid-cols-[max-content_1fr] gap-x-6 gap-y-2 text-sm", className)}>
      {items.map(([k, v], i) => (
        <div key={i} className="contents">
          <dt className="text-muted-foreground">{k}</dt>
          <dd className="min-w-0 break-words">{v ?? "—"}</dd>
        </div>
      ))}
    </dl>
  )
}
