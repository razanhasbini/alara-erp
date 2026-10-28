import { Link } from "react-router"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { useAuth } from "@/auth/auth"
import { Section } from "@/components/common/page"
import { StockStatusPill } from "@/components/common/StatusBadge"
import { api } from "@/lib/api"
import { number } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Box } from "@/types/api"

/** How many boxes are left for these products (and the products inside these bundles). Shows only; changes nothing. */
export function BoxesLeft({ productIds, title = "Boxes left", note }: { productIds: string[]; title?: string; note?: string }) {
  const { can } = useAuth()
  const enabled = can(P.boxes.view) && productIds.length > 0
  const { data } = useQuery({
    queryKey: qk.boxesFor(productIds),
    queryFn: () => api<Box[]>("/api/boxes/for-products", { query: { productIds } }),
    enabled, placeholderData: keepPreviousData,
  })
  if (!enabled || !data?.length) return null
  return (
    <Section title={title} bodyClassName="p-0">
      <ul className="divide-y divide-hairline text-sm">
        {data.map((box) => (
          <li key={box.id} className="flex items-center justify-between gap-3 px-4 py-2">
            <Link to={`/boxes?box=${box.id}`} className="min-w-0 truncate hover:underline">{box.name}</Link>
            <span className="flex shrink-0 items-center gap-2">
              {box.status !== "InStock" && <StockStatusPill status={box.status} />}
              <span className={cn("tabular font-medium", box.status === "Out" && "text-tone-red", box.status === "Low" && "text-tone-amber")}>{number(box.onHand)} left</span>
            </span>
          </li>
        ))}
      </ul>
      {note && <p className="border-t border-hairline px-4 py-2 text-xs text-muted-foreground">{note}</p>}
    </Section>
  )
}
