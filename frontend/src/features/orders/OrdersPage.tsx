import { useCallback } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { Plus } from "lucide-react"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { SearchInput, Segmented } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { OrderStatusPill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { api } from "@/lib/api"
import { money, when } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk } from "@/lib/queries"
import { SOURCES } from "@/lib/rules"
import type { OrderListItem, OrderSource, OrderStatus, Paged } from "@/types/api"
import { formatPhone } from "@/lib/phone"
import { useLogView } from "@/lib/activity"

export function OrdersPage() {
  useLogView("Orders")
  const { can } = useAuth()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const search = params.get("search") ?? ""
  const customerId = params.get("customerId") ?? ""
  const status = (params.get("status") ?? (customerId ? "all" : "open")) as OrderStatus | "open" | "all"
  const source = (params.get("source") ?? "") as OrderSource | ""
  const page = Number(params.get("page") ?? 1)

  const set = useCallback((patch: Record<string, string>) => setParams((prev) => {
    const next = new URLSearchParams(prev)
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k) }
    if (!("page" in patch)) next.delete("page")
    return next
  }, { replace: true }), [setParams])

  // "Sold" = every order that isn't cancelled or returned (an order is sold, and paid, when it's saved).
  const query = {
    search, page, pageSize: 25, customerId: customerId || undefined, source: source || undefined,
    status: status === "open" || status === "all" ? undefined : status,
    statuses: status === "open" ? ["Draft", "Reserved", "Confirmed"] : undefined,
  }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.orders(query),
    queryFn: () => api<Paged<OrderListItem>>("/api/orders", { query }),
    placeholderData: keepPreviousData,
  })

  const columns: Column<OrderListItem>[] = [
    { key: "number", header: "Order", cell: (o) => <div><span className="code font-semibold">{o.orderNumber}</span><div className="text-xs text-muted-foreground">{when(o.createdAt)}</div></div> },
    { key: "customer", header: "Customer", cell: (o) => <div className="min-w-0"><div className="truncate">{o.recipientName}</div><div className="text-xs text-muted-foreground">{formatPhone(o.recipientPhone)}{o.recipientCity && ` · ${o.recipientCity}`}</div></div> },
    { key: "source", header: "Channel", hideOnMobile: true, cell: (o) => <span className="text-muted-foreground">{o.source}</span> },
    { key: "items", header: "Items", align: "right", hideOnMobile: true, cell: (o) => <span className="tabular">{o.itemCount}</span> },
    { key: "total", header: "Total", align: "right", cell: (o) => <span className="tabular font-medium">{money(o.grandTotal, o.currencyCode)}</span> },
    { key: "status", header: "Status", cell: (o) => <OrderStatusPill status={o.status} /> },
  ]

  return (
    <>
      <PageHeader title="Orders" description={customerId ? "Complete order history for the selected customer."
        : "WhatsApp, Instagram and website orders. Saving an order deducts its stock and counts the sale."}
        actions={can(P.orders.create) && <Button asChild><Link to="/orders/new"><Plus /> New order</Link></Button>} />
      {customerId && <div className="mb-3 flex items-center gap-2 rounded-md border border-hairline bg-card px-3 py-2 text-sm">Showing one customer's orders. <Link to="/customers" className="font-medium text-primary hover:underline">Back to customers</Link></div>}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput value={search} onChange={(v) => set({ search: v })} placeholder="Order no., customer name or phone" />
        <Segmented label="Status" value={["open", "all", "Cancelled", "Returned"].includes(status) ? status : "open"} onChange={(v) => set({ status: v === "open" ? "" : v })}
          options={[{ value: "open", label: "Sold" }, { value: "Cancelled", label: "Cancelled" }, { value: "Returned", label: "Returned" }, { value: "all", label: "All" }]} />
        <Select value={source || "any"} onValueChange={(v) => set({ source: v === "any" ? "" : v })}>
          <SelectTrigger className="h-8 w-36 bg-card" aria-label="Channel"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="any">Any channel</SelectItem>
            {SOURCES.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>
      <DataTable columns={columns} rows={data?.items} rowKey={(o) => o.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={(o) => navigate(`/orders/${o.id}`)}
        rowClassName={(o) => (o.status === "Cancelled" ? "opacity-60" : undefined)}
        empty={<EmptyState title={status === "open" ? "No orders yet" : "No orders found"}
          action={can(P.orders.create) && status === "open" && <Button asChild size="sm"><Link to="/orders/new"><Plus /> New order</Link></Button>}>
          {status === "open" ? "New orders from WhatsApp, Instagram or the website will show here." : "Try a different filter."}
        </EmptyState>}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(p) => set({ page: String(p) })} />} />
    </>
  )
}
