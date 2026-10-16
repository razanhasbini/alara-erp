import type { ReactNode } from "react"
import { Info } from "lucide-react"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import { cn } from "@/lib/utils"

/** A headline figure with its exact definition one hover away. */
export function Figure({ label, value, hint, sub, tone }: { label: string; value: ReactNode; hint: string; sub?: ReactNode; tone?: "warn" | "bad" | "good" }) {
  return (
    <div className="min-w-0 px-3 py-4 first:pl-0 md:px-5">
      <div className="flex items-center gap-1">
        <span className="eyebrow">{label}</span>
        <Tooltip>
          <TooltipTrigger asChild><button type="button" aria-label={`How "${label}" is calculated`} className="text-muted-foreground/70 hover:text-foreground"><Info className="size-3" /></button></TooltipTrigger>
          <TooltipContent className="max-w-64">{hint}</TooltipContent>
        </Tooltip>
      </div>
      <div className={cn("font-display mt-1 truncate text-[1.45rem] leading-none tabular md:text-[1.9rem]",
        tone === "warn" && "text-tone-amber", tone === "bad" && "text-tone-red", tone === "good" && "text-tone-green")}>{value}</div>
      {sub && <div className="mt-1.5 text-xs text-muted-foreground">{sub}</div>}
    </div>
  )
}

export function FigureRow({ children, columns = 4 }: { children: ReactNode; columns?: 4 | 5 }) {
  return (
    <div className={cn("grid grid-cols-[repeat(2,minmax(0,1fr))] divide-hairline border-y border-hairline md:divide-x",
      "[&>*:nth-child(n+3)]:border-t [&>*:nth-child(n+3)]:border-hairline md:[&>*:nth-child(n+3)]:border-t-0",
      columns === 5 ? "md:grid-cols-[repeat(5,minmax(0,1fr))]" : "md:grid-cols-[repeat(4,minmax(0,1fr))]")}>
      {children}
    </div>
  )
}
