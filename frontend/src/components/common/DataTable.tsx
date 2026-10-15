import type { ReactNode } from "react"
import { ChevronLeft, ChevronRight } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import { EmptyState, ErrorState } from "@/components/common/states"
import { number } from "@/lib/format"
import { cn } from "@/lib/utils"

export interface Column<T> {
  key: string
  header: ReactNode
  cell: (row: T) => ReactNode
  align?: "left" | "right" | "center"
  className?: string
  /** Hidden below the md breakpoint to keep phone layouts readable. */
  hideOnMobile?: boolean
}

/**
 * Dense, hairline-ruled table. Horizontal scroll on narrow screens; rows are links when `onRowClick` is set.
 */
export function DataTable<T>({ columns, rows, rowKey, loading, error, onRetry, empty, onRowClick, footer, className, rowClassName }: {
  columns: Column<T>[]
  rows: T[] | undefined
  rowKey: (row: T) => string
  loading?: boolean
  error?: unknown
  onRetry?: () => void
  empty?: ReactNode
  onRowClick?: (row: T) => void
  footer?: ReactNode
  className?: string
  rowClassName?: (row: T) => string | undefined
}) {
  const align = (a?: string) => (a === "right" ? "text-right" : a === "center" ? "text-center" : "text-left")
  return (
    <div className={cn("overflow-hidden rounded-lg border border-hairline bg-card", className)}>
      {error ? (
        <div className="p-4"><ErrorState error={error} onRetry={onRetry} /></div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-hairline bg-secondary/40">
                {columns.map((c) => (
                  <th key={c.key} scope="col"
                    className={cn("eyebrow h-9 px-3 font-semibold whitespace-nowrap", align(c.align), c.hideOnMobile && "hidden md:table-cell", c.className)}>
                    {c.header}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {loading && !rows?.length
                ? Array.from({ length: 6 }).map((_, i) => (
                    <tr key={i} className="border-b border-hairline last:border-0">
                      {columns.map((c) => (
                        <td key={c.key} className={cn("px-3 py-3", c.hideOnMobile && "hidden md:table-cell")}><Skeleton className="h-4 w-full max-w-40" /></td>
                      ))}
                    </tr>
                  ))
                : rows?.map((row) => (
                    <tr key={rowKey(row)}
                      onClick={onRowClick ? () => onRowClick(row) : undefined}
                      onKeyDown={onRowClick ? (e) => { if (e.key === "Enter") onRowClick(row) } : undefined}
                      tabIndex={onRowClick ? 0 : undefined}
                      className={cn("border-b border-hairline last:border-0",
                        onRowClick && "cursor-pointer hover:bg-accent focus-visible:bg-accent focus-visible:outline-none",
                        rowClassName?.(row))}>
                      {columns.map((c) => (
                        <td key={c.key} className={cn("px-3 py-2.5 align-middle", align(c.align), c.hideOnMobile && "hidden md:table-cell", c.className)}>
                          {c.cell(row)}
                        </td>
                      ))}
                    </tr>
                  ))}
            </tbody>
          </table>
          {!loading && rows?.length === 0 && (empty ?? <EmptyState title="Nothing here yet" />)}
        </div>
      )}
      {footer}
    </div>
  )
}

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(total, page * pageSize)
  return (
    <div className="flex items-center justify-between gap-3 border-t border-hairline px-3 py-2 text-xs text-muted-foreground">
      <span className="tabular">{number(from)}–{number(to)} of {number(total)}</span>
      <div className="flex items-center gap-1">
        <Button variant="ghost" size="icon-sm" aria-label="Previous page" disabled={page <= 1} onClick={() => onPage(page - 1)}><ChevronLeft /></Button>
        <span className="tabular px-1">Page {page} of {pages}</span>
        <Button variant="ghost" size="icon-sm" aria-label="Next page" disabled={page >= pages} onClick={() => onPage(page + 1)}><ChevronRight /></Button>
      </div>
    </div>
  )
}
