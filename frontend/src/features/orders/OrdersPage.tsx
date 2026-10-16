import { useCallback } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { AlarmClock, Plus } from "lucide-react"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { SearchInput, Segmented } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { OrderStatusPill, PaymentStatusPill, Pill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { api } from "@/lib/api"
import { dateTime, humanize, money, when } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk } from "@/lib/queries"
import { SOURCES } from "@/lib/rules"
import type { OrderListItem, OrderSource, OrderStatus, Paged, PaymentStatus } from "@/types/api"

function ReservedUntil({ until }: { until: string | null }) {
  if (!until) return <span className="text-muted-foreground">No time set</span>
  const expired = new Date(until).getTime() <= Date.now()
  return <div className="flex flex-wrap items-center gap-2">
    <span className={expired ? "font-medium text-tone-red" : undefined}>{dateTime(until)}</span>
    {expired && <Pill tone="red" dot={false}><AlarmClock className="size-3" aria-hidden /> Time is up</Pill>}
  </div>
}

export function OrdersPage() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const search = params.get("search") ?? ""
  const customerId = params.get("customerId") ?? ""
  const status = (params.get("status") ?? (customerId ? "all" : "open")) as OrderStatus | "open" | "all"
  const paymentStatus = (params.get("payment") ?? "") as PaymentStatus | ""
  const source = (params.get("source") ?? "") as OrderSource | ""
  const page = Number(params.get("page") ?? 1)

  const set = useCallback((patch: Record<string, string>) => setParams((prev) => {
    const next = new URLSearchParams(prev)
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k) }
    if (!("page" in patch)) next.delete("page")
    return next
  }, { replace: true }), [setParams])

  // "Open" = sold and still moving: to pack and out for delivery. Reservations have their own section.
  const reservedView = status === "Reserved"
  const query = {
    search, page, pageSize: 25, customerId: customerId || undefined, paymentStatus: paymentStatus || undefined, source: source || undefined,
    status: status === "open" || status === "all" ? undefined : status,
    statuses: status === "open" ? ["Draft", "Confirmed", "Shipped"] : undefined,
    sort: reservedView ? "reservedUntil" : undefined,
  }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.orders(query),
    queryFn: () => api<Paged<OrderListItem>>("/api/orders", { query }),
    placeholderData: keepPreviousData,
  })

  const columns: Column<OrderListItem>[] = [
    { key: "number", header: "Order", cell: (o) => <div><span className="code font-semibold">{o.orderNumber}</span><div className="text-xs text-muted-foreground">{when(o.createdAt)}</div></div> },
    { key: "customer", header: "Customer", cell: (o) => <div className="min-w-0"><div className="truncate">{o.recipientName}</div><div className="text-xs text-muted-foreground">{o.recipientPhone}{o.recipientCity && ` · ${o.recipientCity}`}</div></div> },
    { key: "source", header: "Channel", hideOnMobile: true, cell: (o) => <span className="text-muted-foreground">{o.source}</span> },
    { key: "items", header: "Items", align: "right", hideOnMobile: true, cell: (o) => <span className="tabular">{o.itemCount}</span> },
    { key: "total", header: "Total", align: "right", cell: (o) => <span className="tabular font-medium">{money(o.grandTotal, o.currencyCode)}</span> },
    ...(reservedView
      ? [{ key: "until", header: "Reserved until", cell: (o: OrderListItem) => <ReservedUntil until={o.reservedUntil} /> } satisfies Column<OrderListItem>]
      : [{ key: "status", header: "Status", cell: (o: OrderListItem) => <OrderStatusPill status={o.status} /> } satisfies Column<OrderListItem>]),
    { key: "payment", header: "Payment", hideOnMobile: true, cell: (o) => <PaymentStatusPill status={o.paymentStatus} /> },
    { key: "courier", header: "Courier", hideOnMobile: true, cell: (o) => <div className="text-xs">{o.deliveryCompanyName ?? <span className="text-muted-foreground">—</span>}{o.trackingNumber && <div className="code text-muted-foreground">{o.trackingNumber}</div>}</div> },
  ]

  return (
    <>
      <PageHeader title="Orders" description={customerId ? "Complete order history for the selected customer."
        : reservedView ? "Stock held for customers who haven't bought yet. Nothing here counts as a sale until you mark it as sold."
        : "WhatsApp, Instagram and website orders. Selling deducts stock immediately; shipping details are added later."}
        actions={can(P.orders.create) && <Button asChild><Link to="/orders/new"><Plus /> New order</Link></Button>} />
      {customerId && <div className="mb-3 flex items-center gap-2 rounded-md border border-hairline bg-card px-3 py-2 text-sm">Showing one customer's orders. <Link to="/customers" className="font-medium text-primary hover:underline">Back to customers</Link></div>}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput value={search} onChange={(v) => set({ search: v })} placeholder="Order no., name, phone, tracking" />
        <Segmented label="Status" value={["open", "all", "Reserved", "Delivered", "Cancelled", "Returned"].includes(status) ? status : "all"} onChange={(v) => set({ status: v === "open" ? "" : v })}
          options={[{ value: "open", label: "Open" }, { value: "Reserved", label: "Reserved" }, { value: "Delivered", label: "Delivered" }, { value: "Cancelled", label: "Cancelled" }, { value: "Returned", label: "Returned" }, { value: "all", label: "All" }]} />
        {!reservedView && <Select value={["Draft", "Confirmed", "Shipped"].includes(status) ? status : "any"} onValueChange={(v) => set({ status: v === "any" ? "" : v })}>
          <SelectTrigger className="h-8 w-40 bg-card" aria-label="Open status"><SelectValue placeholder="Stage" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="any">Any stage</SelectItem>
            <SelectItem value="Confirmed">Sold · to pack</SelectItem>
            <SelectItem value="Shipped">Out for delivery</SelectItem>
          </SelectContent>
        </Select>}
        <Select value={paymentStatus || "any"} onValueChange={(v) => set({ payment: v === "any" ? "" : v })}>
          <SelectTrigger className="h-8 w-40 bg-card" aria-label="Payment"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="any">Any payment</SelectItem>
            {(["Unpaid", "CollectedByCourier", "Paid", "Void"] as const).map((s) => <SelectItem key={s} value={s}>{humanize(s)}</SelectItem>)}
          </SelectContent>
        </Select>
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
        empty={<EmptyState title={status === "open" ? "No open orders" : reservedView ? "No reservations" : "No orders found"}
          action={can(P.orders.create) && status === "open" && <Button asChild size="sm"><Link to="/orders/new"><Plus /> New order</Link></Button>}>
          {status === "open" ? "New orders from WhatsApp, Instagram or the website will show here."
            : reservedView ? "Choose “Reserve…” when creating an order to hold stock for a customer without selling it yet." : "Try a different filter."}
        </EmptyState>}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(p) => set({ page: String(p) })} />} />
    </>
  )
}
