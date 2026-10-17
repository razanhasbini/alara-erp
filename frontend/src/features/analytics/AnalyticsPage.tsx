import { useMemo, useState, type ReactNode } from "react"
import { Link, useSearchParams } from "react-router"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis, type TooltipContentProps } from "recharts"
import { AlertTriangle, ArrowDown, ArrowUp, Gift, PackageMinus } from "lucide-react"
import { Figure, FigureRow } from "@/components/common/figures"
import { DataTable, type Column } from "@/components/common/DataTable"
import { Segmented } from "@/components/common/controls"
import { PageHeader, Section } from "@/components/common/page"
import { EmptyState, ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { api } from "@/lib/api"
import { dateOnly, money, number, percent, todayIso, when } from "@/lib/format"
import { qk } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { AnalyticsSummary, BundleProfit, CustomerAnalytics, CustomerLoyalty, GiftGiven, Grouping, ProductAnalytics, ProductProfit } from "@/types/api"
import { PRESETS, presetRange, type Preset } from "./range"

// Single series (net sales) in the brand rose; passes the dataviz contrast check on the light surface.
const BAR_COLOR = "#b8306f"
const GRID = "#efe9e7"
const INK_MUTED = "#6f6567"

type Tab = "overview" | "products" | "customers" | "gifts"

export function AnalyticsPage() {
  const [params, setParams] = useSearchParams()
  const preset = (params.get("range") ?? "last-30") as Preset
  const defaults = presetRange(preset)
  const from = params.get("from") ?? defaults.from
  const to = params.get("to") ?? defaults.to
  const groupBy = (params.get("group") ?? defaults.groupBy) as Grouping
  const tab = (["overview", "products", "customers", "gifts"].includes(params.get("tab") ?? "") ? params.get("tab") : "overview") as Tab
  const isCustom = preset === "custom" || (params.has("from") && !params.has("range"))
  const query = { from, to, groupBy }

  const setParam = (patch: Record<string, string | null>) => setParams((prev) => {
    const next = new URLSearchParams(prev)
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k) }
    return next
  }, { replace: true })

  const summary = useQuery({ queryKey: qk.analytics("summary", query), queryFn: () => api<AnalyticsSummary>("/api/analytics/summary", { query }), placeholderData: keepPreviousData })
  const products = useQuery({
    queryKey: qk.analytics("products", { from, to }), queryFn: () => api<ProductAnalytics>("/api/analytics/products", { query: { from, to } }),
    placeholderData: keepPreviousData, enabled: tab === "products",
  })
  const customers = useQuery({
    queryKey: qk.analytics("customers", { from, to }), queryFn: () => api<CustomerAnalytics>("/api/analytics/customers", { query: { from, to } }),
    placeholderData: keepPreviousData, enabled: tab === "customers",
  })

  const s = summary.data
  const giftCount = s?.gifts?.length ?? 0
  return (
    <>
      <PageHeader title="Sales & profit" description="How the business is doing over any period. A sale counts on the day an order is sold."
        meta={summary.isFetching && <span className="flex items-center gap-1.5 text-xs text-muted-foreground"><InlineSpinner className="size-3" /> Updating</span>} />

      <div className="mb-5 flex flex-wrap items-end gap-2">
        <div className="grid gap-1">
          <label htmlFor="an-preset" className="eyebrow">Period</label>
          <Select value={isCustom ? "custom" : preset}
            onValueChange={(v) => setParam(v === "custom" ? { range: "custom", from, to } : { range: v, from: null, to: null, group: null })}>
            <SelectTrigger id="an-preset" className="h-8 w-40 bg-card"><SelectValue /></SelectTrigger>
            <SelectContent>{PRESETS.map((p) => <SelectItem key={p.value} value={p.value}>{p.label}</SelectItem>)}</SelectContent>
          </Select>
        </div>
        {isCustom && <>
          <div className="grid gap-1">
            <label htmlFor="an-from" className="eyebrow">From</label>
            <Input id="an-from" type="date" value={from} max={to} className="h-8 w-38 bg-card"
              onChange={(e) => e.target.value && setParam({ range: "custom", from: e.target.value, to })} />
          </div>
          <div className="grid gap-1">
            <label htmlFor="an-to" className="eyebrow">To</label>
            <Input id="an-to" type="date" value={to} min={from} max={todayIso()} className="h-8 w-38 bg-card"
              onChange={(e) => e.target.value && setParam({ range: "custom", from, to: e.target.value })} />
          </div>
        </>}
        <span className="pb-1.5 text-sm text-muted-foreground">{from === to ? dateOnly(from) : `${dateOnly(from)} – ${dateOnly(to)}`}</span>
      </div>

      <nav aria-label="Analytics sections" className="mb-6 flex gap-1 border-b border-hairline">
        {([["overview", "Overview"], ["products", "Products"], ["customers", "Customers"], ["gifts", giftCount ? `Gifts · ${giftCount}` : "Gifts"]] as [Tab, string][]).map(([value, label]) => (
          <button key={value} type="button" aria-current={tab === value ? "page" : undefined} onClick={() => setParam({ tab: value === "overview" ? null : value })}
            className={cn("-mb-px border-b-2 px-3 py-2 text-sm transition-colors",
              tab === value ? "border-rose font-medium text-foreground" : "border-transparent text-muted-foreground hover:text-foreground")}>
            {label}
          </button>
        ))}
      </nav>

      {summary.isLoading ? <FullPageSpinner /> : summary.error ? <ErrorState error={summary.error} onRetry={summary.refetch} /> : s && (
        <>
          {tab === "overview" && <Overview s={s} groupBy={groupBy} onGroupBy={(g) => setParam({ group: g })} onGifts={() => setParam({ tab: "gifts" })} />}
          {tab === "products" && <ProductTables data={products.data} loading={products.isLoading} error={products.error} />}
          {tab === "customers" && <CustomerLoyaltyTable data={customers.data} loading={customers.isLoading} error={customers.error} />}
          {tab === "gifts" && <GiftsList s={s} />}
        </>
      )}
    </>
  )
}

// ---- overview -------------------------------------------------------------------------------

function Overview({ s, groupBy, onGroupBy, onGifts }: { s: AnalyticsSummary; groupBy: Grouping; onGroupBy: (g: Grouping) => void; onGifts: () => void }) {
  const t = s.totals
  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-6">
      <FigureRow>
        <Figure label="Sales" value={money(t.netSales)} sub={`${number(t.orders)} order${t.orders === 1 ? "" : "s"} · avg ${money(t.averageOrderValue)}`}
          hint="What customers paid for their items, after discounts. Cancelled and returned orders are not included." />
        {s.includesCosts
          ? <Figure label="Net profit" value={money(t.profit)} tone={(t.profit ?? 0) < 0 ? "bad" : (t.profit ?? 0) > 0 ? "good" : undefined}
              sub={`${percent(t.netMarginPercent)} of sales`} hint="What the business kept: sales minus what the products cost, gifts and bills." />
          : <Figure label="Orders" value={number(t.orders)} sub={`avg ${money(t.averageOrderValue)}`} hint="Orders sold in this period." />}
        <Figure label="Units sold" value={number(t.unitsSold)} sub="incl. products inside bundles" hint="Product units customers paid for. Free gifts are counted separately." />
        <Figure label="Gifts given" value={number(t.giftUnits)} sub={s.includesCosts ? `costing ${money(t.gifts)}` : "free items"}
          hint="Free items: gift lines on orders, plus stock taken out as a sample or gift (e.g. a PR box)." />
      </FigureRow>

      {s.includesCosts && t.uncostedUnits > 0 && (
        <p role="alert" className="flex items-start gap-2 rounded-md border border-tone-amber/20 bg-tone-amber-bg px-4 py-2.5 text-sm text-tone-amber">
          <AlertTriangle className="mt-0.5 size-4 shrink-0" />
          <span>{t.uncostedUnits} unit{t.uncostedUnits === 1 ? "" : "s"} in this period {t.uncostedUnits === 1 ? "has" : "have"} no cost yet, so profit looks higher than it is. Add the cost on those products.</span>
        </p>
      )}

      <div className="grid grid-cols-[minmax(0,1fr)] gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="grid content-start gap-6">
          {s.includesCosts && <ProfitStatement s={s} onGifts={onGifts} />}
          <SalesChart s={s} groupBy={groupBy} onGroupBy={onGroupBy} />
        </div>
        <div className="grid content-start gap-6">
          <ChannelTable s={s} />
          {s.expensesByCategory && <BillsByCategory s={s} />}
        </div>
      </div>
    </div>
  )
}

/** Sales → net profit, one line at a time, each explained in plain words. */
function ProfitStatement({ s, onGifts }: { s: AnalyticsSummary; onGifts: () => void }) {
  const t = s.totals
  const line = (label: ReactNode, value: number | null, explain: ReactNode, opts: { minus?: boolean; total?: boolean; tone?: "good" | "bad" } = {}) => (
    <div className={cn("grid grid-cols-[minmax(0,1fr)_auto] items-baseline gap-x-4 px-4 py-3", opts.total && "bg-secondary/40")}>
      <div className="min-w-0">
        <div className={cn("text-sm", opts.total ? "font-semibold" : "font-medium")}>{label}</div>
        <div className="mt-0.5 text-xs text-muted-foreground">{explain}</div>
      </div>
      <div className={cn("tabular text-right", opts.total ? "font-display text-2xl" : "text-base",
        opts.tone === "good" && "text-tone-green", opts.tone === "bad" && "text-tone-red")}>
        {opts.minus && value ? "−" : ""}{money(value)}
      </div>
    </div>
  )
  const net = t.profit ?? 0
  return (
    <Section title="Where the money went" bodyClassName="divide-y divide-hairline">
      {line("Sales", t.netSales, `${number(t.orders)} order${t.orders === 1 ? "" : "s"}, after discounts`)}
      {line("What the products cost", t.costOfGoods, `What you paid for the ${number(t.unitsSold)} unit${t.unitsSold === 1 ? "" : "s"} customers bought`, { minus: true })}
      {line("Gross profit", t.grossProfit, `${percent(t.grossMarginPercent)} of sales is left after paying for the products`, { total: true, tone: (t.grossProfit ?? 0) < 0 ? "bad" : undefined })}
      {line(<button type="button" className="font-medium hover:underline" onClick={onGifts}>Gifts</button>, t.gifts,
        t.giftUnits ? `${number(t.giftUnits)} free item${t.giftUnits === 1 ? "" : "s"}: gifts with orders and samples or PR boxes` : "Nothing given away in this period", { minus: true })}
      {line(<Link to="/bills" className="hover:underline">Bills</Link>, t.expenses, "Electricity, Wi-Fi, models, wages and other bills dated in this period", { minus: true })}
      {line("Net profit", t.profit, `${percent(t.netMarginPercent)} of sales: what the business kept`, { total: true, tone: net < 0 ? "bad" : net > 0 ? "good" : undefined })}
    </Section>
  )
}

function ChartTooltip({ active, payload, includesCosts }: TooltipContentProps<number, string> & { includesCosts: boolean }) {
  if (!active || !payload?.length) return null
  const p = payload[0].payload as AnalyticsSummary["series"][number]
  return (
    <div className="grid gap-0.5 rounded-md border border-hairline bg-popover px-3 py-2 text-xs shadow-sm">
      <div className="font-medium">{p.periodStart === p.periodEnd ? dateOnly(p.periodStart) : `${dateOnly(p.periodStart)} – ${dateOnly(p.periodEnd)}`}</div>
      <div className="tabular">Sales {money(p.netSales)} · {p.orders} order{p.orders === 1 ? "" : "s"}</div>
      {includesCosts && <div className={cn("tabular", (p.profit ?? 0) < 0 && "text-tone-red")}>Net profit {money(p.profit)}</div>}
    </div>
  )
}

function SalesChart({ s, groupBy, onGroupBy }: { s: AnalyticsSummary; groupBy: Grouping; onGroupBy: (g: Grouping) => void }) {
  const [asTable, setAsTable] = useState(false)
  const label = (d: string) => new Intl.DateTimeFormat("en-GB", s.groupBy === "Month"
    ? { month: "short", year: "2-digit", timeZone: "UTC" } : { day: "numeric", month: "short", timeZone: "UTC" }).format(new Date(`${d}T00:00:00Z`))
  const data = s.series.map((p) => ({ ...p, label: label(p.periodStart) }))
  const interval = Math.max(0, Math.ceil(data.length / 10) - 1)
  const periodLabel = (p: AnalyticsSummary["series"][number]) => p.periodStart === p.periodEnd ? dateOnly(p.periodStart) : `${dateOnly(p.periodStart)} – ${dateOnly(p.periodEnd)}`
  return (
    <Section title={`Sales by ${groupBy.toLowerCase()}`}
      actions={<div className="flex items-center gap-3">
        <Segmented label="Group by" value={groupBy} onChange={onGroupBy}
          options={[{ value: "Day", label: "Day" }, { value: "Week", label: "Week" }, { value: "Month", label: "Month" }]} />
        <button type="button" className="text-xs text-muted-foreground hover:text-foreground" onClick={() => setAsTable((v) => !v)}>{asTable ? "Chart" : "Table"}</button>
      </div>}
      bodyClassName="p-4">
      {asTable ? (
        <div className="max-h-80 overflow-auto">
          <table className="w-full text-sm">
            <thead><tr className="border-b border-hairline">
              <th className="eyebrow py-1.5 text-left">Period</th><th className="eyebrow py-1.5 text-right">Orders</th><th className="eyebrow py-1.5 text-right">Sales</th>
              {s.includesCosts && <><th className="eyebrow py-1.5 text-right">Gifts</th><th className="eyebrow py-1.5 text-right">Bills</th><th className="eyebrow py-1.5 text-right">Net profit</th></>}
            </tr></thead>
            <tbody>{[...s.series].reverse().filter((p) => p.orders || p.gifts || p.expenses).map((p) => (
              <tr key={p.periodStart} className="border-b border-hairline last:border-0">
                <td className="py-1.5">{periodLabel(p)}</td>
                <td className="py-1.5 text-right tabular">{p.orders}</td><td className="py-1.5 text-right tabular">{money(p.netSales)}</td>
                {s.includesCosts && <>
                  <td className="py-1.5 text-right tabular text-muted-foreground">{p.gifts ? money(p.gifts) : "—"}</td>
                  <td className="py-1.5 text-right tabular text-muted-foreground">{p.expenses ? money(p.expenses) : "—"}</td>
                  <td className={cn("py-1.5 text-right tabular font-medium", (p.profit ?? 0) < 0 && "text-tone-red")}>{money(p.profit)}</td>
                </>}
              </tr>
            ))}</tbody>
          </table>
        </div>
      ) : (
        <div className="h-60" role="img" aria-label={`Sales by ${s.groupBy.toLowerCase()}, total ${money(s.totals.netSales)}`}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data} margin={{ top: 8, right: 4, bottom: 0, left: 0 }} barCategoryGap="22%">
              <CartesianGrid vertical={false} stroke={GRID} />
              <XAxis dataKey="label" interval={interval} tickLine={false} axisLine={{ stroke: GRID }} tick={{ fill: INK_MUTED, fontSize: 11 }} />
              <YAxis width={48} tickLine={false} axisLine={false} tick={{ fill: INK_MUTED, fontSize: 11 }}
                tickFormatter={(v: number) => (v >= 1000 ? `$${(v / 1000).toFixed(v % 1000 ? 1 : 0)}K` : `$${v}`)} />
              <Tooltip cursor={{ fill: "#f6f0ee" }} content={(props) => <ChartTooltip {...(props as TooltipContentProps<number, string>)} includesCosts={s.includesCosts} />} />
              <Bar dataKey="netSales" fill={BAR_COLOR} radius={[4, 4, 0, 0]} maxBarSize={28} isAnimationActive={false} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </Section>
  )
}

const BILL_LABELS: Record<string, string> = { Electricity: "Electricity", Internet: "Internet / Wi-Fi", Models: "Models", Wages: "Wages", Other: "Other" }

function BillsByCategory({ s }: { s: AnalyticsSummary }) {
  const rows = (s.expensesByCategory ?? []).filter((r) => r.amount > 0)
  return (
    <Section title="Bills" actions={<Link to="/bills" className="text-xs font-medium text-primary hover:underline">Manage →</Link>}>
      {rows.length === 0 ? <p className="px-4 py-3 text-sm text-muted-foreground">No bills dated in this period.</p> : (
        <table className="w-full text-sm">
          <tbody>{rows.map((r) => (
            <tr key={r.category} className="border-b border-hairline last:border-0">
              <td className="px-4 py-2">{BILL_LABELS[r.category] ?? r.category}</td>
              <td className="px-4 py-2 text-right tabular">{money(r.amount)}</td>
            </tr>
          ))}</tbody>
        </table>
      )}
    </Section>
  )
}

function ChannelTable({ s }: { s: AnalyticsSummary }) {
  const rows = s.byChannel.filter((c) => c.orders > 0)
  return (
    <Section title="Where orders came from">
      {rows.length === 0 ? <p className="px-4 py-3 text-sm text-muted-foreground">No sales in this period.</p> : (
        <table className="w-full text-sm">
          <tbody>{rows.map((c) => (
            <tr key={c.source} className="border-b border-hairline last:border-0">
              <td className="px-4 py-2">{c.source}<div className="text-xs text-muted-foreground">{c.orders} order{c.orders === 1 ? "" : "s"}</div></td>
              <td className="px-4 py-2 text-right tabular">{money(c.netSales)}
                {s.includesCosts && <div className="text-xs text-muted-foreground">{money(c.grossProfit)} gross profit</div>}</td>
            </tr>
          ))}</tbody>
        </table>
      )}
    </Section>
  )
}

// ---- gifts ----------------------------------------------------------------------------------

function GiftsList({ s }: { s: AnalyticsSummary }) {
  const gifts = s.gifts ?? []
  const columns: Column<GiftGiven>[] = [
    { key: "date", header: "Date", cell: (g) => <span className="tabular">{dateOnly(g.at.slice(0, 10))}</span> },
    {
      key: "what", header: "What was given",
      cell: (g) => <div className="flex items-start gap-2">
        {g.source === "Order" ? <Gift className="mt-0.5 size-4 shrink-0 text-rose" aria-hidden /> : <PackageMinus className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />}
        <span>{g.description}</span>
      </div>,
    },
    {
      key: "to", header: "Given to",
      cell: (g) => g.source === "Order"
        ? <div><div>{g.recipient}</div><Link to={`/orders/${g.orderId}`} className="code text-xs text-primary hover:underline">{g.orderNumber}</Link></div>
        : <span className="text-muted-foreground">Taken out of stock</span>,
    },
    { key: "units", header: "Items", align: "right", cell: (g) => <span className="tabular">{g.units}</span> },
    ...(s.includesCosts ? [{
      key: "cost", header: "Cost", align: "right" as const,
      cell: (g: GiftGiven) => g.cost === null ? <span className="text-tone-amber">no cost yet</span> : <span className="tabular font-medium">{money(g.cost)}</span>,
    }] : []),
  ]
  return (
    <div className="grid gap-4">
      <p className="text-sm text-muted-foreground">
        Everything given for free in this period: gifts added to orders, and stock taken out as a sample or gift (for example a PR box, from a bundle's "Take out of stock").
        {s.includesCosts && <> Their cost, <span className="font-medium text-foreground tabular">{money(s.totals.gifts)}</span>, is subtracted from net profit.</>}
      </p>
      <DataTable columns={columns} rows={gifts} rowKey={(g) => `${g.source}-${g.orderId ?? g.at}-${g.description}`}
        empty={<EmptyState title="No gifts in this period">Add a gift to an order, or take a bundle out of stock as a sample or gift.</EmptyState>} />
    </div>
  )
}

// ---- products & customers -------------------------------------------------------------------

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
  return { sorted, header }
}

function CustomerLoyaltyTable({ data, loading, error }: { data?: CustomerAnalytics; loading: boolean; error: unknown }) {
  const customers = useSort<CustomerLoyalty>(data?.customers, "orderCount")
  const columns: Column<CustomerLoyalty>[] = [
    { key: "customer", header: customers.header("name", "Customer"), cell: (row) => <div className="min-w-0"><Link to={`/customers?search=${encodeURIComponent(row.phone)}`} className="font-medium hover:underline">{row.name}</Link><div className="text-xs text-muted-foreground">{row.phone}{row.city && ` · ${row.city}`}</div></div> },
    { key: "orders", header: customers.header("orderCount", "Orders"), align: "right", cell: (row) => <Link to={`/orders?customerId=${row.customerId}`} className="tabular font-semibold text-primary hover:underline">{row.orderCount}</Link> },
    { key: "spent", header: customers.header("totalOrdered", "Spent"), align: "right", cell: (row) => <span className="tabular font-medium">{money(row.totalOrdered)}</span> },
    { key: "average", header: customers.header("averageOrderValue", "Avg order"), align: "right", hideOnMobile: true, cell: (row) => <span className="tabular">{money(row.averageOrderValue)}</span> },
    { key: "frequency", header: customers.header("averageDaysBetweenOrders", "Orders every"), align: "right", hideOnMobile: true, cell: (row) => <span className="tabular">{row.averageDaysBetweenOrders === null ? "—" : `${number(row.averageDaysBetweenOrders)} days`}</span> },
    { key: "last", header: customers.header("lastOrderAt", "Last order"), hideOnMobile: true, cell: (row) => when(row.lastOrderAt) },
  ]
  return (
    <div className="grid gap-3">
      <p className="text-sm text-muted-foreground">Customers who bought in this period. Click a column to sort. "Orders every" is the average time between their orders.</p>
      <DataTable columns={columns} rows={customers.sorted} rowKey={(row) => row.customerId} loading={loading} error={error}
        empty={<EmptyState title="No customer orders in this period" />} />
    </div>
  )
}

function ProductTables({ data, loading, error }: { data?: ProductAnalytics; loading: boolean; error: unknown }) {
  const [view, setView] = useState<"products" | "bundles">("products")
  const [showAll, setShowAll] = useState(false)
  const productRows = useMemo(() => (data?.products ?? []).filter((r) => showAll || r.unitsSold > 0), [data, showAll])
  const bundleRows = useMemo(() => (data?.bundles ?? []).filter((r) => showAll || r.setsSold > 0), [data, showAll])
  const p = useSort<ProductProfit>(productRows, "unitsSold")
  const b = useSort<BundleProfit>(bundleRows, "setsSold")
  const costs = !!data?.includesCosts
  const profitCell = (v: number | null, pct: number | null) => v === null ? "—" : (
    <span className={cn("tabular", v < 0 && "text-tone-red")}>{money(v)} <span className="text-xs text-muted-foreground">{percent(pct, 0)}</span></span>)

  const productColumns: Column<ProductProfit>[] = [
    { key: "name", header: p.header("name", "Product"), cell: (r) => <Link to={`/products/${r.productId}`} className="hover:underline">{r.name}</Link> },
    { key: "units", header: p.header("unitsSold", "Sold"), align: "right", cell: (r) => <span className="tabular">{r.unitsSold}{r.unitsSoldInBundles > 0 && <span className="text-xs text-muted-foreground"> ({r.unitsSoldInBundles} in sets)</span>}</span> },
    { key: "sales", header: p.header("netSales", "Sales"), align: "right", cell: (r) => <span className="tabular">{money(r.netSales)}</span> },
    ...(costs ? [{ key: "gp", header: p.header("grossProfit", "Gross profit"), align: "right" as const, cell: (r: ProductProfit) => profitCell(r.grossProfit, r.grossMarginPercent) }] : []),
  ]
  const bundleColumns: Column<BundleProfit>[] = [
    { key: "name", header: b.header("name", "Bundle"), cell: (r) => <Link to={`/products/${r.productId}`} className="hover:underline">{r.name}</Link> },
    { key: "sold", header: b.header("setsSold", "Sets sold"), align: "right", cell: (r) => <span className="tabular">{r.setsSold}</span> },
    { key: "sales", header: b.header("netSales", "Sales"), align: "right", cell: (r) => <span className="tabular">{money(r.netSales)}</span> },
    ...(costs ? [{ key: "gp", header: b.header("grossProfit", "Gross profit"), align: "right" as const, cell: (r: BundleProfit) => profitCell(r.grossProfit, r.grossMarginPercent) }] : []),
  ]

  return (
    <div className="grid content-start gap-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Segmented label="Table" value={view} onChange={setView} options={[{ value: "products", label: "Products" }, { value: "bundles", label: "Bundles" }]} />
        <label className="flex items-center gap-2 text-sm text-muted-foreground">
          <Switch checked={showAll} onCheckedChange={setShowAll} /> Include items with no sales
        </label>
      </div>
      <p className="text-sm text-muted-foreground">
        {view === "products" ? "Products inside bundles are included, with their share of the bundle's price." : "Bundles as sets."} Free gifts aren't counted here; see the Gifts tab. Click a column to sort.
      </p>
      {view === "products"
        ? <DataTable columns={productColumns} rows={p.sorted} rowKey={(r) => r.productId} loading={loading} error={error}
            empty={<EmptyState title="No products sold in this period" />} />
        : <DataTable columns={bundleColumns} rows={b.sorted} rowKey={(r) => r.productId} loading={loading} error={error}
            empty={<EmptyState title="No bundles sold in this period" />} />}
    </div>
  )
}
