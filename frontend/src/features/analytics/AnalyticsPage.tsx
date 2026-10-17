import { useMemo, useState, type ReactNode } from "react"
import { Link, useSearchParams } from "react-router"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis, type TooltipContentProps } from "recharts"
import { AlertTriangle, ArrowDown, ArrowUp } from "lucide-react"
import { Figure, FigureRow } from "@/components/common/figures"
import { DataTable, type Column } from "@/components/common/DataTable"
import { Segmented } from "@/components/common/controls"
import { PageHeader, Section } from "@/components/common/page"
import { ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { api } from "@/lib/api"
import { cost, dateOnly, money, number, percent, todayIso, when } from "@/lib/format"
import { qk } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { AnalyticsSummary, BundleProfit, CustomerAnalytics, CustomerLoyalty, Grouping, ProductAnalytics, ProductProfit } from "@/types/api"
import { PRESETS, presetRange, type Preset } from "./range"

// Validated pair (dataviz validator, light surface): passes chroma, CVD and contrast checks.
const COST_COLOR = "#3b7fc4"
const PROFIT_COLOR = "#b8306f"
const GRID = "#efe9e7"
const INK_MUTED = "#6f6567"

export function AnalyticsPage() {
  const [params, setParams] = useSearchParams()
  const preset = (params.get("range") ?? "last-30") as Preset
  const defaults = presetRange(preset)
  const from = params.get("from") ?? defaults.from
  const to = params.get("to") ?? defaults.to
  const groupBy = (params.get("group") ?? defaults.groupBy) as Grouping
  const query = { from, to, groupBy }

  const setRange = (patch: Record<string, string | null>) => setParams((prev) => {
    const next = new URLSearchParams(prev)
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k) }
    return next
  }, { replace: true })

  const summary = useQuery({ queryKey: qk.analytics("summary", query), queryFn: () => api<AnalyticsSummary>("/api/analytics/summary", { query }), placeholderData: keepPreviousData })
  const products = useQuery({ queryKey: qk.analytics("products", { from, to }), queryFn: () => api<ProductAnalytics>("/api/analytics/products", { query: { from, to } }), placeholderData: keepPreviousData })
  const customers = useQuery({ queryKey: qk.analytics("customers", { from, to }), queryFn: () => api<CustomerAnalytics>("/api/analytics/customers", { query: { from, to } }), placeholderData: keepPreviousData })

  const s = summary.data
  return (
    <>
      <PageHeader title="Analytics" description="Sales, cost and profit for any period. A sale counts when an order is sold. Cancelled and returned orders are excluded."
        meta={summary.isFetching && <span className="flex items-center gap-1.5 text-xs text-muted-foreground"><InlineSpinner className="size-3" /> Updating</span>} />

      <div className="mb-6 flex flex-wrap items-end gap-2">
        <div className="grid gap-1">
          <label htmlFor="an-preset" className="eyebrow">Period</label>
          <Select value={params.has("from") && !params.has("range") ? "custom" : preset}
            onValueChange={(v) => setRange(v === "custom" ? { range: "custom" } : { range: v, from: null, to: null, group: null })}>
            <SelectTrigger id="an-preset" className="h-8 w-40 bg-card"><SelectValue /></SelectTrigger>
            <SelectContent>{PRESETS.map((p) => <SelectItem key={p.value} value={p.value}>{p.label}</SelectItem>)}</SelectContent>
          </Select>
        </div>
        <div className="grid gap-1">
          <label htmlFor="an-from" className="eyebrow">From</label>
          <Input id="an-from" type="date" value={from} max={to} className="h-8 w-38 bg-card"
            onChange={(e) => e.target.value && setRange({ range: "custom", from: e.target.value, to })} />
        </div>
        <div className="grid gap-1">
          <label htmlFor="an-to" className="eyebrow">To</label>
          <Input id="an-to" type="date" value={to} min={from} max={todayIso()} className="h-8 w-38 bg-card"
            onChange={(e) => e.target.value && setRange({ range: "custom", from, to: e.target.value })} />
        </div>
        <div className="grid gap-1">
          <span className="eyebrow">Show by</span>
          <Segmented label="Group by" value={groupBy} onChange={(v) => setRange({ group: v })}
            options={[{ value: "Day", label: "Day" }, { value: "Week", label: "Week" }, { value: "Month", label: "Month" }]} />
        </div>
      </div>

      {summary.isLoading ? <FullPageSpinner /> : summary.error ? <ErrorState error={summary.error} onRetry={summary.refetch} /> : s && (
        <div className="grid grid-cols-[minmax(0,1fr)] gap-6">
          <Totals s={s} />
          {s.includesCosts && s.totals.uncostedUnits > 0 && (
            <p role="alert" className="flex items-start gap-2 rounded-md border border-tone-amber/20 bg-tone-amber-bg px-4 py-2.5 text-sm text-tone-amber">
              <AlertTriangle className="mt-0.5 size-4 shrink-0" />
              <span>{s.totals.uncostedUnits} unit(s) sold in this period have no cost recorded, so profit is overstated. Set a standard cost on those products or enter costs when receiving stock.</span>
            </p>
          )}
          <PeriodChart s={s} />
          <CustomerLoyaltyTable data={customers.data} loading={customers.isLoading} error={customers.error} />
          <div className="grid grid-cols-[minmax(0,1fr)] gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
            <ProductTables data={products.data} loading={products.isLoading} error={products.error} />
            <div className="grid content-start gap-6">
              <ChannelTable s={s} />
              {s.expensesByCategory && <BillsByCategory s={s} />}
              <Section title="How these numbers work" bodyClassName="grid gap-2 p-4 text-xs text-muted-foreground">
                <p><b className="text-foreground">Net sales</b>: items minus discounts.</p>
                {s.includesCosts && <>
                  <p><b className="text-foreground">Cost of goods</b>: what the units that left actually cost, from the inventory batch they were taken from.</p>
                  <p><b className="text-foreground">Gross profit</b>: net sales − cost of goods.</p>
                  <p><b className="text-foreground">Net profit</b>: gross profit − the bills dated in the period (electricity, Wi-Fi, models, wages…) from <Link to="/bills" className="text-primary hover:underline">Bills &amp; expenses</Link>.</p>
                </>}
                <p><b className="text-foreground">Cash received</b>: the full order total, counted on the day the order is sold.</p>
                <p>Returned orders are not sales. Bundle revenue is shared across the products inside it by their full prices.</p>
              </Section>
            </div>
          </div>
        </div>
      )}
    </>
  )
}

function Totals({ s }: { s: AnalyticsSummary }) {
  const t = s.totals
  const range = s.from === s.to ? dateOnly(s.from) : `${dateOnly(s.from)} – ${dateOnly(s.to)}`
  if (!s.includesCosts) {
    return (
      <FigureRow>
        <Figure label="Net sales" value={money(t.netSales)} sub={range} hint="Items minus discounts from orders sold in this period. Cancelled and returned orders are excluded." />
        <Figure label="Orders" value={number(t.orders)} sub={`avg ${money(t.averageOrderValue)}`} hint="Orders sold in this period (not cancelled or returned)." />
        <Figure label="Units sold" value={number(t.unitsSold)} sub="incl. units inside bundles" hint="Product units in sold orders, including the products inside bundles." />
        <Figure label="Cash received" value={money(t.cashReceived)} hint="The full total of orders sold in this period. Reserved orders are excluded." />
      </FigureRow>
    )
  }
  return (
    <FigureRow columns={5}>
      <Figure label="Net sales" value={money(t.netSales)} sub={`${number(t.orders)} order${t.orders === 1 ? "" : "s"} · avg ${money(t.averageOrderValue)}`}
        hint="Items minus discounts from orders sold in this period. Cancelled and returned orders are excluded." />
      <Figure label="Cost of goods" value={money(t.costOfGoods)} sub={`${number(t.unitsSold)} units sold`}
        hint="What the sold units cost ÀLara Line, from the exact deliveries the stock was taken from when each order was sold." />
      <Figure label="Gross profit" value={money(t.grossProfit)} tone={(t.grossProfit ?? 0) < 0 ? "bad" : undefined} sub={`${percent(t.grossMarginPercent)} margin`}
        hint="Net sales − cost of goods. Margin = gross profit ÷ net sales." />
      <Figure label="Bills" value={money(t.expenses)} sub={<Link to="/bills" className="hover:underline">Bills &amp; expenses →</Link>}
        hint="Electricity, Wi-Fi, models, wages and other bills dated in this period." />
      <Figure label="Net profit" value={money(t.profit)} tone={(t.profit ?? 0) < 0 ? "bad" : (t.profit ?? 0) > 0 ? "good" : undefined}
        sub={`${percent(t.netMarginPercent)} of sales`}
        hint="Gross profit − bills. What the business actually earned in this period." />
    </FigureRow>
  )
}

function ChartTooltip({ active, payload, includesCosts }: TooltipContentProps<number, string> & { includesCosts: boolean }) {
  if (!active || !payload?.length) return null
  const p = payload[0].payload as AnalyticsSummary["series"][number]
  return (
    <div className="grid gap-0.5 rounded-md border border-hairline bg-popover px-3 py-2 text-xs shadow-sm">
      <div className="font-medium">{p.periodStart === p.periodEnd ? dateOnly(p.periodStart) : `${dateOnly(p.periodStart)} – ${dateOnly(p.periodEnd)}`}</div>
      <div className="tabular">Net sales {money(p.netSales)} · {p.orders} order{p.orders === 1 ? "" : "s"}</div>
      {includesCosts && <>
        <div className="flex items-center gap-1.5 tabular"><span className="size-2 rounded-sm" style={{ background: COST_COLOR }} />Cost of goods {money(p.costOfGoods)}</div>
        <div className="flex items-center gap-1.5 tabular"><span className="size-2 rounded-sm" style={{ background: PROFIT_COLOR }} />Gross profit {money(p.grossProfit)}</div>
      </>}
      <div className="tabular text-muted-foreground">Cash received {money(p.cashReceived)}</div>
    </div>
  )
}

function PeriodChart({ s }: { s: AnalyticsSummary }) {
  const [asTable, setAsTable] = useState(false)
  const label = (d: string) => new Intl.DateTimeFormat("en-GB", s.groupBy === "Month"
    ? { month: "short", year: "2-digit", timeZone: "UTC" } : { day: "numeric", month: "short", timeZone: "UTC" }).format(new Date(`${d}T00:00:00Z`))
  // Stacked: cost + gross profit = net sales (a loss shows as cost only, the loss is in the tooltip/table).
  const data = s.series.map((p) => ({ ...p, label: label(p.periodStart), costBar: p.costOfGoods ?? 0, profitBar: Math.max(0, p.grossProfit ?? 0) }))
  const interval = Math.max(0, Math.ceil(data.length / 10) - 1)
  return (
    <Section
      title={<span>{s.includesCosts ? "Net sales split into cost and gross profit" : "Net sales"} <span className="font-normal text-muted-foreground">by {s.groupBy.toLowerCase()}</span></span>}
      actions={<button type="button" className="text-xs text-muted-foreground hover:text-foreground" onClick={() => setAsTable((t) => !t)}>{asTable ? "Show chart" : "Show table"}</button>}
      bodyClassName="p-4">
      {asTable ? (
        <div className="max-h-80 overflow-auto">
          <table className="w-full text-sm">
            <thead><tr className="border-b border-hairline">
              <th className="eyebrow py-1.5 text-left">Period</th><th className="eyebrow py-1.5 text-right">Orders</th><th className="eyebrow py-1.5 text-right">Net sales</th>
              {s.includesCosts && <><th className="eyebrow py-1.5 text-right">Cost</th><th className="eyebrow py-1.5 text-right">Gross profit</th><th className="eyebrow py-1.5 text-right">Bills</th><th className="eyebrow py-1.5 text-right">Net profit</th></>}
              <th className="eyebrow py-1.5 text-right">Cash in</th>
            </tr></thead>
            <tbody>{[...s.series].reverse().map((p) => (
              <tr key={p.periodStart} className="border-b border-hairline last:border-0">
                <td className="py-1.5">{p.periodStart === p.periodEnd ? dateOnly(p.periodStart) : `${dateOnly(p.periodStart)} – ${dateOnly(p.periodEnd)}`}</td>
                <td className="py-1.5 text-right tabular">{p.orders}</td><td className="py-1.5 text-right tabular">{money(p.netSales)}</td>
                {s.includesCosts && <><td className="py-1.5 text-right tabular">{money(p.costOfGoods)}</td>
                  <td className={cn("py-1.5 text-right tabular", (p.grossProfit ?? 0) < 0 && "text-tone-red")}>{money(p.grossProfit)}</td>
                  <td className="py-1.5 text-right tabular text-muted-foreground">{p.expenses ? money(p.expenses) : "—"}</td>
                  <td className={cn("py-1.5 text-right tabular", (p.profit ?? 0) < 0 && "text-tone-red")}>{money(p.profit)}</td></>}
                <td className="py-1.5 text-right tabular">{money(p.cashReceived)}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      ) : (
        <>
          {s.includesCosts && (
            <div className="mb-2 flex gap-4 text-xs text-muted-foreground" aria-hidden>
              <span className="flex items-center gap-1.5"><span className="size-2.5 rounded-sm" style={{ background: COST_COLOR }} />Cost of goods</span>
              <span className="flex items-center gap-1.5"><span className="size-2.5 rounded-sm" style={{ background: PROFIT_COLOR }} />Gross profit</span>
            </div>
          )}
          <div className="h-64" role="img" aria-label={`Net sales by ${s.groupBy.toLowerCase()}, total ${money(s.totals.netSales)}`}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={data} margin={{ top: 8, right: 4, bottom: 0, left: 0 }} barCategoryGap="22%">
                <CartesianGrid vertical={false} stroke={GRID} />
                <XAxis dataKey="label" interval={interval} tickLine={false} axisLine={{ stroke: GRID }} tick={{ fill: INK_MUTED, fontSize: 11 }} />
                <YAxis width={48} tickLine={false} axisLine={false} tick={{ fill: INK_MUTED, fontSize: 11 }}
                  tickFormatter={(v: number) => (v >= 1000 ? `$${(v / 1000).toFixed(v % 1000 ? 1 : 0)}K` : `$${v}`)} />
                <Tooltip cursor={{ fill: "#f6f0ee" }} content={(props) => <ChartTooltip {...(props as TooltipContentProps<number, string>)} includesCosts={s.includesCosts} />} />
                {s.includesCosts ? <>
                  <Bar dataKey="costBar" stackId="s" fill={COST_COLOR} maxBarSize={28} isAnimationActive={false} stroke="#fff" strokeWidth={1} />
                  <Bar dataKey="profitBar" stackId="s" fill={PROFIT_COLOR} radius={[4, 4, 0, 0]} maxBarSize={28} isAnimationActive={false} stroke="#fff" strokeWidth={1} />
                </> : <Bar dataKey="netSales" fill={PROFIT_COLOR} radius={[4, 4, 0, 0]} maxBarSize={28} isAnimationActive={false} />}
              </BarChart>
            </ResponsiveContainer>
          </div>
        </>
      )}
    </Section>
  )
}

const BILL_LABELS: Record<string, string> = { Electricity: "Electricity", Internet: "Internet / Wi-Fi", Models: "Models", Wages: "Wages", Other: "Other" }

function BillsByCategory({ s }: { s: AnalyticsSummary }) {
  const rows = s.expensesByCategory ?? []
  const total = rows.reduce((sum, r) => sum + r.amount, 0)
  return (
    <Section title="Bills by category" actions={<Link to="/bills" className="text-xs font-medium text-primary hover:underline">Manage →</Link>}>
      <table className="w-full text-sm">
        <tbody>{rows.map((r) => (
          <tr key={r.category} className="border-b border-hairline last:border-0">
            <td className="px-4 py-2">{BILL_LABELS[r.category] ?? r.category}</td>
            <td className={cn("px-4 py-2 text-right tabular", !r.amount && "text-muted-foreground")}>{r.amount ? money(r.amount) : "—"}</td>
          </tr>
        ))}</tbody>
        <tfoot><tr className="border-t border-hairline bg-secondary/40 font-medium">
          <td className="px-4 py-2">Total</td><td className="px-4 py-2 text-right tabular">{money(total)}</td>
        </tr></tfoot>
      </table>
    </Section>
  )
}

function ChannelTable({ s }: { s: AnalyticsSummary }) {
  return (
    <Section title="By channel">
      <table className="w-full text-sm">
        <thead><tr className="border-b border-hairline"><th className="eyebrow px-4 py-2 text-left">Channel</th><th className="eyebrow px-2 py-2 text-right">Orders</th>
          <th className="eyebrow px-2 py-2 text-right">Net sales</th>{s.includesCosts && <th className="eyebrow px-4 py-2 text-right">Gross profit</th>}</tr></thead>
        <tbody>{s.byChannel.map((c) => (
          <tr key={c.source} className="border-b border-hairline last:border-0">
            <td className="px-4 py-2">{c.source}</td><td className="px-2 py-2 text-right tabular">{c.orders}</td>
            <td className="px-2 py-2 text-right tabular">{money(c.netSales)}</td>
            {s.includesCosts && <td className="px-4 py-2 text-right tabular">{money(c.grossProfit)}</td>}
          </tr>
        ))}</tbody>
      </table>
    </Section>
  )
}

type SortDir = "asc" | "desc"
function useSort<T>(rows: T[] | undefined, initial: keyof T) {
  const [key, setKey] = useState<keyof T>(initial)
  const [dir, setDir] = useState<SortDir>("desc")
  const sorted = useMemo(() => [...(rows ?? [])].sort((a, b) => {
    const x = a[key] as unknown, y = b[key] as unknown
    if (x === null || x === undefined) return y === null || y === undefined ? 0 : 1
    if (y === null || y === undefined) return -1
    const cmp = typeof x === "string" && typeof y === "string" ? x.localeCompare(y) : (x as number) - (y as number)
    return dir === "asc" ? cmp : -cmp
  }), [rows, key, dir])
  const header = (k: keyof T, label: ReactNode) => (
    <button type="button" className="inline-flex items-center gap-1 uppercase hover:text-foreground"
      onClick={() => { if (k === key) setDir(dir === "asc" ? "desc" : "asc"); else { setKey(k); setDir("desc") } }}>
      {label}{key === k && (dir === "asc" ? <ArrowUp className="size-3" /> : <ArrowDown className="size-3" />)}
    </button>
  )
  const setSort = (nextKey: keyof T, nextDir: SortDir) => { setKey(nextKey); setDir(nextDir) }
  return { sorted, header, key, dir, setSort }
}

function CustomerLoyaltyTable({ data, loading, error }: { data?: CustomerAnalytics; loading: boolean; error: unknown }) {
  const customers = useSort<CustomerLoyalty>(data?.customers, "orderCount")
  const value = `${String(customers.key)}:${customers.dir}`
  const columns: Column<CustomerLoyalty>[] = [
    { key: "customer", header: customers.header("name", "Customer"), cell: (row) => <div className="min-w-0"><Link to={`/customers?search=${encodeURIComponent(row.phone)}`} className="font-medium hover:underline">{row.name}</Link><div className="text-xs text-muted-foreground">{row.phone}{row.city && ` · ${row.city}`}</div></div> },
    { key: "orders", header: customers.header("orderCount", "Orders"), align: "right", cell: (row) => <Link to={`/orders?customerId=${row.customerId}`} className="tabular font-semibold text-primary hover:underline">{row.orderCount}</Link> },
    { key: "repeat", header: customers.header("repeatOrders", "Repeat orders"), align: "right", hideOnMobile: true, cell: (row) => <span className="tabular">{row.repeatOrders}</span> },
    { key: "ordered", header: customers.header("totalOrdered", "Total ordered"), align: "right", cell: (row) => <span className="tabular font-medium">{money(row.totalOrdered)}</span> },
    { key: "paid", header: customers.header("totalPaid", "Paid"), align: "right", cell: (row) => <span className="tabular">{money(row.totalPaid)}</span> },
    { key: "average", header: customers.header("averageOrderValue", "Avg order"), align: "right", hideOnMobile: true, cell: (row) => <span className="tabular">{money(row.averageOrderValue)}</span> },
    { key: "frequency", header: customers.header("averageDaysBetweenOrders", "Avg reorder"), align: "right", cell: (row) => <span className="tabular">{row.averageDaysBetweenOrders === null ? "First order" : `${number(row.averageDaysBetweenOrders)} days`}</span> },
    { key: "last", header: customers.header("lastOrderAt", "Last order"), hideOnMobile: true, cell: (row) => when(row.lastOrderAt) },
  ]
  return (
    <section className="grid gap-3" aria-labelledby="loyal-customers-title">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div><h2 id="loyal-customers-title" className="text-base font-semibold">Most loyal customers</h2>
          <p className="mt-0.5 text-sm text-muted-foreground">Confirmed orders in this period. “Avg reorder” is the average time between purchases; a smaller number means they order more often.</p></div>
        <Select value={value} onValueChange={(next) => { const [key, direction] = next.split(":"); customers.setSort(key as keyof CustomerLoyalty, direction as SortDir) }}>
          <SelectTrigger className="w-52 bg-card" aria-label="Rank customers"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="orderCount:desc">Most orders first</SelectItem>
            <SelectItem value="totalOrdered:desc">Highest total ordered</SelectItem>
            <SelectItem value="totalPaid:desc">Highest amount paid</SelectItem>
            <SelectItem value="averageOrderValue:desc">Highest average order</SelectItem>
            <SelectItem value="averageDaysBetweenOrders:asc">Orders most often</SelectItem>
            <SelectItem value="lastOrderAt:desc">Most recent order</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <DataTable columns={columns} rows={customers.sorted} rowKey={(row) => row.customerId} loading={loading} error={error}
        empty={<div className="p-8 text-center text-sm text-muted-foreground">No confirmed customer orders in this period.</div>} />
    </section>
  )
}

function ProductTables({ data, loading, error }: { data?: ProductAnalytics; loading: boolean; error: unknown }) {
  const [view, setView] = useState<"products" | "bundles">("products")
  const p = useSort<ProductProfit>(data?.products, "unitsSold")
  const b = useSort<BundleProfit>(data?.bundles, "setsSold")
  const costs = !!data?.includesCosts
  const marginCell = (v: number | null, pct: number | null) => v === null ? "—" : (
    <span className={cn("tabular", v < 0 && "text-tone-red")}>{money(v)} <span className="text-xs text-muted-foreground">{percent(pct, 0)}</span></span>)

  const productColumns: Column<ProductProfit>[] = [
    { key: "name", header: p.header("name", "Product"), cell: (r) => <Link to={`/products/${r.productId}`} className="hover:underline">{r.name}</Link> },
    { key: "price", header: p.header("sellingPrice", "Price"), align: "right", hideOnMobile: true, cell: (r) => <span className="tabular">{money(r.sellingPrice)}</span> },
    ...(costs ? [
      { key: "cost", header: p.header("averageCost", "Unit cost"), align: "right" as const, cell: (r: ProductProfit) => r.averageCost === null ? <span className="text-tone-amber">not set</span> : <span className="tabular">{cost(r.averageCost)}</span> },
      { key: "um", header: p.header("unitMargin", "Earn / unit"), align: "right" as const, hideOnMobile: true, cell: (r: ProductProfit) => marginCell(r.unitMargin, r.unitMarginPercent) },
    ] : []),
    { key: "units", header: p.header("unitsSold", "Sold"), align: "right", cell: (r) => <span className="tabular" title={r.unitsSoldInBundles ? `${r.unitsSoldInBundles} inside bundles` : undefined}>{r.unitsSold}{r.unitsSoldInBundles > 0 && <span className="text-xs text-muted-foreground"> ({r.unitsSoldInBundles} in sets)</span>}</span> },
    { key: "sales", header: p.header("netSales", "Net sales"), align: "right", cell: (r) => <span className="tabular">{money(r.netSales)}</span> },
    ...(costs ? [
      { key: "gp", header: p.header("grossProfit", "Gross profit"), align: "right" as const, cell: (r: ProductProfit) => marginCell(r.grossProfit, r.grossMarginPercent) },
    ] : []),
  ]
  const bundleColumns: Column<BundleProfit>[] = [
    { key: "name", header: b.header("name", "Bundle"), cell: (r) => <Link to={`/products/${r.productId}`} className="hover:underline">{r.name}</Link> },
    { key: "price", header: b.header("sellingPrice", "Price"), align: "right", cell: (r) => <span className="tabular">{money(r.sellingPrice)}</span> },
    ...(costs ? [
      { key: "cost", header: b.header("componentsCost", "Set cost"), align: "right" as const, cell: (r: BundleProfit) => r.componentsCost === null ? <span className="text-tone-amber">incomplete</span> : <span className="tabular">{money(r.componentsCost)}</span> },
      { key: "um", header: b.header("unitMargin", "Earn / set"), align: "right" as const, hideOnMobile: true, cell: (r: BundleProfit) => marginCell(r.unitMargin, r.unitMarginPercent) },
    ] : []),
    { key: "sold", header: b.header("setsSold", "Sold"), align: "right", cell: (r) => <span className="tabular">{r.setsSold}</span> },
    { key: "sales", header: b.header("netSales", "Net sales"), align: "right", cell: (r) => <span className="tabular">{money(r.netSales)}</span> },
    ...(costs ? [{ key: "gp", header: b.header("grossProfit", "Gross profit"), align: "right" as const, cell: (r: BundleProfit) => marginCell(r.grossProfit, r.grossMarginPercent) }] : []),
  ]

  const productSort = `${String(p.key)}:${p.dir}`
  const bundleSort = `${String(b.key)}:${b.dir}`
  const chooseProductSort = (value: string) => {
    const [key, dir] = value.split(":")
    p.setSort(key as keyof ProductProfit, dir as SortDir)
  }
  const chooseBundleSort = (value: string) => {
    const [key, dir] = value.split(":")
    b.setSort(key as keyof BundleProfit, dir as SortDir)
  }

  return (
    <div className="grid content-start gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Segmented label="Table" value={view} onChange={setView} options={[{ value: "products", label: "Products" }, { value: "bundles", label: "Bundles" }]} />
        {view === "products" ? (
          <Select value={productSort} onValueChange={chooseProductSort}>
            <SelectTrigger className="h-8 w-48 bg-card" aria-label="Rank products"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="unitsSold:desc">Most sold first</SelectItem>
              <SelectItem value="unitsSold:asc">Least sold first</SelectItem>
              <SelectItem value="netSales:desc">Highest net sales</SelectItem>
              {costs && <SelectItem value="grossProfit:desc">Highest gross profit</SelectItem>}
              {costs && <SelectItem value="unitMarginPercent:desc">Best unit margin</SelectItem>}
              <SelectItem value="sellingPrice:desc">Highest price</SelectItem>
              <SelectItem value="name:asc">Name A–Z</SelectItem>
            </SelectContent>
          </Select>
        ) : (
          <Select value={bundleSort} onValueChange={chooseBundleSort}>
            <SelectTrigger className="h-8 w-48 bg-card" aria-label="Rank bundles"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="setsSold:desc">Most sets sold first</SelectItem>
              <SelectItem value="setsSold:asc">Least sets sold first</SelectItem>
              <SelectItem value="netSales:desc">Highest net sales</SelectItem>
              {costs && <SelectItem value="grossProfit:desc">Highest gross profit</SelectItem>}
              {costs && <SelectItem value="unitMarginPercent:desc">Best set margin</SelectItem>}
              <SelectItem value="sellingPrice:desc">Highest price</SelectItem>
              <SelectItem value="name:asc">Name A–Z</SelectItem>
            </SelectContent>
          </Select>
        )}
      </div>
      {view === "products"
        ? <DataTable columns={productColumns} rows={p.sorted} rowKey={(r) => r.productId} loading={loading} error={error}
            rowClassName={(r) => (r.unitsSold === 0 ? "text-muted-foreground" : undefined)} />
        : <DataTable columns={bundleColumns} rows={b.sorted} rowKey={(r) => r.productId} loading={loading} error={error}
            rowClassName={(r) => (r.setsSold === 0 ? "text-muted-foreground" : undefined)} />}
      {costs && <p className="text-xs text-muted-foreground">“Unit cost” and “Earn / unit” use today's stock cost (or the standard cost if nothing is in stock). Sales columns cover the selected period only.</p>}
    </div>
  )
}
