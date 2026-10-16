import { Link, useSearchParams } from "react-router"
import { MovementReferenceLink } from "./MovementReferenceLink"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { PageHeader } from "@/components/common/page"
import { Pill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { api } from "@/lib/api"
import { humanize, when } from "@/lib/format"
import { qk, useWarehouses } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Movement, MovementType, Paged } from "@/types/api"

const typeLabel: Record<MovementType, string> = {
  Receipt: "Received", Shipment: "Order confirmed", Return: "Returned", AdjustmentIncrease: "Adjusted +", AdjustmentDecrease: "Adjusted −",
  TransferOut: "Transfer out", TransferIn: "Transfer in",
}

export function MovementsPage() {
  const [params, setParams] = useSearchParams()
  const productId = params.get("product") ?? undefined
  const warehouseId = params.get("warehouse") ?? undefined
  const type = (params.get("type") ?? undefined) as MovementType | undefined
  const page = Number(params.get("page") ?? 1)
  const { data: warehouses } = useWarehouses(true)
  const query = { productId, warehouseId, type, page, pageSize: 50 }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.movements(query), queryFn: () => api<Paged<Movement>>("/api/inventory/movements", { query }), placeholderData: keepPreviousData,
  })
  const set = (k: string, v?: string) => setParams((prev) => { const n = new URLSearchParams(prev); if (v) n.set(k, v); else n.delete(k); if (k !== "page") n.delete("page"); return n }, { replace: true })

  const columns: Column<Movement>[] = [
    { key: "when", header: "When", cell: (m) => <span className="whitespace-nowrap">{when(m.createdAt)}</span> },
    { key: "product", header: "Product", cell: (m) => <div><Link to={`/products/${m.productId}`} className="hover:underline">{m.productName}</Link><div className="text-xs text-muted-foreground">Batch {m.batchNumber}</div></div> },
    { key: "type", header: "Movement", cell: (m) => <span className="whitespace-nowrap">{typeLabel[m.type]}{m.reason && <span className="text-muted-foreground"> · {humanize(m.reason)}</span>}</span> },
    { key: "qty", header: "Units", align: "right", cell: (m) => <span className={cn("tabular font-semibold", m.quantityChange > 0 ? "text-tone-green" : "text-tone-red")}>{m.quantityChange > 0 ? `+${m.quantityChange}` : m.quantityChange}</span> },
    { key: "wh", header: "Warehouse", hideOnMobile: true, cell: (m) => <span className="code">{m.warehouseCode}</span> },
    { key: "ref", header: "Reference", cell: (m) => <MovementReferenceLink movement={m} /> },
    { key: "by", header: "By", hideOnMobile: true, cell: (m) => <span className="text-muted-foreground">{m.performedBy ?? "System"}</span> },
    { key: "notes", header: "Notes", hideOnMobile: true, cell: (m) => <span className="line-clamp-1 max-w-56 text-muted-foreground">{m.notes ?? ""}</span> },
  ]

  return (
    <>
      <PageHeader title="Stock movements" description="Every change to stock, as it happened. Entries cannot be edited or deleted."
        meta={productId && <Pill tone="rose" dot={false}>Filtered to one product <Button variant="link" size="xs" className="h-auto p-0 text-rose-ink" onClick={() => set("product")}>clear</Button></Pill>} />
      <div className="mb-3 flex flex-wrap gap-2">
        <Select value={type ?? "all"} onValueChange={(v) => set("type", v === "all" ? undefined : v)}>
          <SelectTrigger className="h-8 w-44 bg-card" aria-label="Movement type"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All movements</SelectItem>
            {Object.entries(typeLabel).map(([k, v]) => <SelectItem key={k} value={k}>{v}</SelectItem>)}
          </SelectContent>
        </Select>
        {(warehouses?.length ?? 0) > 1 && (
          <Select value={warehouseId ?? "all"} onValueChange={(v) => set("warehouse", v === "all" ? undefined : v)}>
            <SelectTrigger className="h-8 w-44 bg-card" aria-label="Warehouse"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All warehouses</SelectItem>
              {warehouses?.map((w) => <SelectItem key={w.id} value={w.id}>{w.code}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
      </div>
      <DataTable columns={columns} rows={data?.items} rowKey={(m) => m.id} loading={isLoading} error={error} onRetry={refetch}
        empty={<EmptyState title="No movements yet">Receipts, shipments, returns, adjustments and transfers are recorded here.</EmptyState>}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(p) => set("page", String(p))} />} />
    </>
  )
}
