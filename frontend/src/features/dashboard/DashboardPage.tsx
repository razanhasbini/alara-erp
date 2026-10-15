import { useState, type ReactNode } from "react"
import { Link } from "react-router"
import { useQuery } from "@tanstack/react-query"
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis, type TooltipContentProps } from "recharts"
import { Info } from "lucide-react"
import { useAuth } from "@/auth/auth"
import { Section } from "@/components/common/page"
import { OrderStatusPill, PaymentStatusPill, Pill } from "@/components/common/StatusBadge"
import { ErrorState, FullPageSpinner } from "@/components/common/states"
import { Tooltip as Hint, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import { api } from "@/lib/api"
import { dateOnly, daysUntil, money, number, when } from "@/lib/format"
import { qk } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Dashboard } from "@/types/api"
import { MovementReferenceLink } from "@/features/inventory/MovementReferenceLink"

const ROSE = "#b8306f"
const GRID = "#efe9e7"
const INK_MUTED = "#6f6567"

function greeting() {
  const h = Number(new Intl.DateTimeFormat("en-GB", { hour: "numeric", hour12: false, timeZone: "Asia/Beirut" }).format(new Date()))
  return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening"
}

/** A headline figure with its exact definition one hover away. */
function Figure({ label, value, hint, sub, tone }: { label: string; value: ReactNode; hint: string; sub?: ReactNode; tone?: "warn" | "bad" }) {
  return (
    <div className="min-w-0 px-3 py-4 first:pl-0 md:px-5">
      <div className="flex items-center gap-1">
        <span className="eyebrow">{label}</span>
        <Hint>
          <TooltipTrigger asChild><button type="button" aria-label={`How "${label}" is calculated`} className="text-muted-foreground/70 hover:text-foreground"><Info className="size-3" /></button></TooltipTrigger>
          <TooltipContent className="max-w-64">{hint}</TooltipContent>
        </Hint>
      </div>
      <div className={cn("font-display mt-1 truncate text-[1.45rem] leading-none tabular md:text-[1.9rem]", tone === "warn" && "text-tone-amber", tone === "bad" && "text-tone-red")}>{value}</div>
      {sub && <div className="mt-1.5 text-xs text-muted-foreground">{sub}</div>}
    </div>
  )
}

function FigureRow({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-[repeat(2,minmax(0,1fr))] divide-hairline border-y border-hairline md:grid-cols-[repeat(4,minmax(0,1fr))] md:divide-x [&>*:nth-child(n+3)]:border-t [&>*:nth-child(n+3)]:border-hairline md:[&>*:nth-child(n+3)]:border-t-0">{children}</div>
}

export function DashboardPage() {
  const { user } = useAuth()
  const [today] = useState(() => ({
    greeting: greeting(),
    label: new Intl.DateTimeFormat("en-GB", { weekday: "long", day: "numeric", month: "long", timeZone: "Asia/Beirut" }).format(new Date()),
  }))
  const { data, isLoading, error, refetch } = useQuery({ queryKey: qk.dashboard, queryFn: () => api<Dashboard>("/api/dashboard"), refetchInterval: 60_000 })
  if (isLoading) return <FullPageSpinner />
  if (error || !data) return <ErrorState error={error} onRetry={refetch} />
  const { finance, orders, inventory, catalog } = data

  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-8">
      <header className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <p className="eyebrow">{today.label}</p>
          <h1 className="font-display mt-1 text-[2.1rem] leading-tight">{today.greeting}, {user?.firstName}</h1>
        </div>
        <p className="text-xs text-muted-foreground">Live figures · updated {when(data.generatedAt)}</p>
      </header>

      {(finance || orders) && (
        <FigureRow>
          {finance && <Figure label="Cash collected" value={money(finance.collectedThisMonth)} sub={`${money(finance.collectedRevenue)} all time`}
            hint="Sum of order totals (incl. delivery fee) whose payment is Paid — cash actually received — paid this calendar month. Cancelled, returned and unpaid orders are excluded." />}
          {finance && <Figure label="With couriers" value={money(finance.awaitingRemittance)} tone={finance.awaitingRemittance > 0 ? "warn" : undefined}
            sub={<Link to="/deliveries" className="hover:underline">Record hand-overs →</Link>}
            hint="Delivered cash-on-delivery orders whose cash the courier has not handed over yet (gross, before courier fees)." />}
          {orders && <Figure label="To pack" value={number(orders.awaitingShipment)} sub={`${orders.drafts} draft${orders.drafts === 1 ? "" : "s"}`}
            hint="Confirmed orders (stock reserved) not yet handed to a courier." />}
          {orders && <Figure label="Out for delivery" value={number(orders.outForDelivery)} sub={`${orders.createdToday} new today`}
            hint="Orders handed to a courier and not yet marked delivered or returned." />}
          {!finance && inventory && <Figure label="Units in stock" value={number(inventory.unitsOnHand)} sub={`${number(inventory.unitsReserved)} reserved`} hint="Physical units on hand across warehouses." />}
          {!finance && catalog && <Figure label="Active products" value={number(catalog.activeProducts)} sub={`${catalog.activeBundles} bundles`} hint="Individual products currently for sale." />}
        </FigureRow>
      )}
      {!finance && !orders && inventory && (
        <FigureRow>
          <Figure label="Units in stock" value={number(inventory.unitsOnHand)} sub={`${number(inventory.unitsReserved)} reserved for orders`} hint="Physical units on hand across warehouses." />
          <Figure label="Low stock" value={number(inventory.lowStockCount)} tone={inventory.lowStockCount ? "warn" : undefined} hint="Products whose available quantity is at or below their alert threshold (but above zero)." />
          <Figure label="Out of stock" value={number(inventory.outOfStockCount)} tone={inventory.outOfStockCount ? "bad" : undefined} hint="Active products with nothing available to sell." />
          <Figure label="Expiring" value={number(inventory.expiringUnits)} tone={inventory.expiringUnits ? "warn" : undefined} hint={`Units in batches expiring within ${inventory.expiryWarningDays} days.`} />
        </FigureRow>
      )}

      {data.dailyOrders && data.bySource && (
        <div className="grid grid-cols-[minmax(0,1fr)] gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
          <DailyChart points={data.dailyOrders} />
          <Section title="By channel · 30 days" bodyClassName="grid gap-3 p-4">
            {(() => {
              const max = Math.max(1, ...data.bySource.map((s) => s.value))
              return data.bySource.map((s) => (
                <div key={s.source}>
                  <div className="mb-1 flex justify-between text-sm"><span>{s.source}</span><span className="tabular text-muted-foreground">{s.orders} · {money(s.value)}</span></div>
                  <div className="h-2.5 rounded-r-[4px] bg-secondary" role="img" aria-label={`${s.source}: ${money(s.value)}`}>
                    <div className="h-full rounded-r-[4px]" style={{ width: `${(s.value / max) * 100}%`, background: ROSE }} />
                  </div>
                </div>
              ))
            })()}
            <p className="text-[0.7rem] text-muted-foreground">Order value excluding drafts, cancelled and returned orders.</p>
          </Section>
        </div>
      )}

      <div className="grid grid-cols-[minmax(0,1fr)] gap-6 lg:grid-cols-2">
        {data.recentOrders && (
          <Section title="Latest orders" actions={<Link to="/orders" className="text-xs text-muted-foreground hover:text-foreground">All orders →</Link>}>
            {data.recentOrders.length === 0 ? <p className="p-4 text-sm text-muted-foreground">No orders yet.</p> : (
              <ul className="divide-y divide-hairline">
                {data.recentOrders.map((o) => (
                  <li key={o.id}>
                    <Link to={`/orders/${o.id}`} className="flex items-center justify-between gap-3 px-4 py-2.5 hover:bg-accent">
                      <span className="min-w-0"><span className="code font-semibold">{o.orderNumber}</span> <span className="text-sm">{o.recipientName}</span>
                        <span className="block text-xs text-muted-foreground">{when(o.createdAt)}</span></span>
                      <span className="flex shrink-0 items-center gap-2"><span className="tabular text-sm">{money(o.grandTotal)}</span><OrderStatusPill status={o.status} /><span className="hidden sm:inline"><PaymentStatusPill status={o.paymentStatus} /></span></span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </Section>
        )}

        {inventory && (
          <Section title="Stock to watch" actions={<Link to="/inventory?filter=Low" className="text-xs text-muted-foreground hover:text-foreground">Inventory →</Link>}>
            <div className="grid grid-cols-3 border-b border-hairline text-center">
              {[["Low", inventory.lowStockCount, "Low"], ["Out", inventory.outOfStockCount, "Out"], [`Expiring ≤ ${inventory.expiryWarningDays}d`, inventory.expiringUnits, "Expiring"]].map(([l, v, f]) => (
                <Link key={l as string} to={`/inventory?filter=${f}`} className="px-2 py-3 hover:bg-accent">
                  <div className={cn("font-display text-xl tabular", (v as number) > 0 && (f === "Out" ? "text-tone-red" : "text-tone-amber"))}>{number(v as number)}</div>
                  <div className="eyebrow">{l}</div>
                </Link>
              ))}
            </div>
            {inventory.expiredUnits > 0 && (
              <Link to="/inventory?filter=Expired" className="block border-b border-hairline bg-tone-red-bg px-4 py-2 text-sm text-tone-red">
                {inventory.expiredUnits} expired unit(s) are still on hand — write them off.
              </Link>
            )}
            <ul className="divide-y divide-hairline text-sm">
              {inventory.lowStock.map((p) => (
                <li key={p.productId} className="flex justify-between px-4 py-2">
                  <Link to={`/inventory?product=${p.productId}`} className="truncate hover:underline">{p.name}</Link>
                  <span className="shrink-0 tabular text-tone-amber">{p.available} left <span className="text-muted-foreground">/ alert {p.threshold}</span></span>
                </li>
              ))}
              {inventory.expiring.map((b) => (
                <li key={`${b.productId}-${b.batchNumber}-${b.warehouseCode}`} className="flex justify-between gap-3 px-4 py-2">
                  <span className="truncate">{b.name} <span className="code text-muted-foreground">{b.batchNumber}</span></span>
                  <span className="shrink-0 tabular">{b.units} · <span className={daysUntil(b.expiryDate) < 0 ? "text-tone-red" : "text-tone-amber"}>{dateOnly(b.expiryDate)}</span></span>
                </li>
              ))}
              {inventory.outOfStockCount > 0 && (
                <li className="px-4 py-2">
                  <Link to="/inventory?filter=Out" className="text-tone-red hover:underline">
                    {inventory.outOfStockCount} product{inventory.outOfStockCount === 1 ? " is" : "s are"} out of stock →
                  </Link>
                </li>
              )}
              {inventory.lowStock.length === 0 && inventory.expiring.length === 0 && inventory.outOfStockCount === 0 && (
                <li className="px-4 py-6 text-center text-muted-foreground">Nothing needs attention.</li>
              )}
            </ul>
          </Section>
        )}
      </div>

      {inventory && (inventory.byWarehouse.length > 1 || data.recentMovements?.length) ? (
        <div className="grid grid-cols-[minmax(0,1fr)] gap-6 lg:grid-cols-[20rem_minmax(0,1fr)]">
          <Section title="Stock by warehouse" bodyClassName="grid gap-3 p-4">
            {inventory.byWarehouse.map((w) => {
              const max = Math.max(1, ...inventory.byWarehouse.map((x) => x.onHand))
              return (
                <div key={w.warehouseId}>
                  <div className="mb-1 flex justify-between text-sm"><span><span className="code">{w.code}</span> {w.name}</span><span className="tabular text-muted-foreground">{number(w.onHand)}</span></div>
                  <div className="h-2.5 rounded-r-[4px] bg-secondary"><div className="h-full rounded-r-[4px]" style={{ width: `${(w.onHand / max) * 100}%`, background: ROSE }} /></div>
                  {w.reserved > 0 && <div className="mt-0.5 text-[0.7rem] text-muted-foreground">{number(w.reserved)} reserved for orders</div>}
                </div>
              )
            })}
          </Section>
          {data.recentMovements && (
            <Section title="Recent stock movements" actions={<Link to="/inventory/movements" className="text-xs text-muted-foreground hover:text-foreground">Ledger →</Link>}>
              {data.recentMovements.length === 0 ? <p className="p-4 text-sm text-muted-foreground">No stock movements yet. Start by receiving stock.</p> : (
                <ul className="divide-y divide-hairline text-sm">
                  {data.recentMovements.map((m) => (
                    <li key={m.id} className="flex items-center justify-between gap-3 px-4 py-2">
                      <span className="min-w-0 truncate">{m.productName} <span className="text-xs text-muted-foreground">· {when(m.createdAt)}</span></span>
                      <span className="flex shrink-0 items-center gap-3">
                        <MovementReferenceLink movement={m} />
                        <span className={cn("w-10 text-right tabular font-semibold", m.quantityChange > 0 ? "text-tone-green" : "text-tone-red")}>{m.quantityChange > 0 ? `+${m.quantityChange}` : m.quantityChange}</span>
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </Section>
          )}
        </div>
      ) : null}

      {catalog && finance && (
        <p className="text-xs text-muted-foreground">
          Catalogue: {catalog.activeProducts} active products, {catalog.activeBundles} bundles{catalog.inactiveProducts ? `, ${catalog.inactiveProducts} inactive` : ""}.
          {" "}<Pill tone="neutral" dot={false}>All amounts in {finance.currency}</Pill>
        </p>
      )}
    </div>
  )
}

function ChartTooltip({ active, payload }: TooltipContentProps<number, string>) {
  if (!active || !payload?.length) return null
  const p = payload[0].payload as { date: string; orders: number; value: number }
  return (
    <div className="rounded-md border border-hairline bg-popover px-3 py-2 text-xs shadow-sm">
      <div className="font-medium">{dateOnly(p.date)}</div>
      <div className="tabular">{money(p.value)} · {p.orders} order{p.orders === 1 ? "" : "s"}</div>
    </div>
  )
}

function DailyChart({ points }: { points: { date: string; orders: number; value: number }[] }) {
  const [asTable, setAsTable] = useState(false)
  const total = points.reduce((s, p) => s + p.value, 0)
  const count = points.reduce((s, p) => s + p.orders, 0)
  const short = (d: string) => new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", timeZone: "UTC" }).format(new Date(`${d}T00:00:00Z`))
  return (
    <Section title={<span>Order value · last 30 days <span className="ml-2 font-normal text-muted-foreground tabular">{money(total)} · {count} order{count === 1 ? "" : "s"}</span></span>}
      actions={<button type="button" className="text-xs text-muted-foreground hover:text-foreground" onClick={() => setAsTable((t) => !t)}>{asTable ? "Show chart" : "Show table"}</button>}
      bodyClassName="p-4">
      {asTable ? (
        <div className="max-h-64 overflow-y-auto">
          <table className="w-full text-sm">
            <thead><tr className="border-b border-hairline"><th className="eyebrow py-1 text-left">Day</th><th className="eyebrow py-1 text-right">Orders</th><th className="eyebrow py-1 text-right">Value</th></tr></thead>
            <tbody>{[...points].reverse().map((p) => <tr key={p.date} className="border-b border-hairline last:border-0"><td className="py-1">{dateOnly(p.date)}</td><td className="py-1 text-right tabular">{p.orders}</td><td className="py-1 text-right tabular">{money(p.value)}</td></tr>)}</tbody>
          </table>
        </div>
      ) : (
        <div className="h-56" role="img" aria-label={`Daily order value for the last 30 days, total ${money(total)}`}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={points} margin={{ top: 8, right: 4, bottom: 0, left: 0 }} barCategoryGap="22%">
              <CartesianGrid vertical={false} stroke={GRID} strokeWidth={1} />
              <XAxis dataKey="date" tickFormatter={short} interval={6} tickLine={false} axisLine={{ stroke: GRID }} tick={{ fill: INK_MUTED, fontSize: 11 }} />
              <YAxis width={44} tickLine={false} axisLine={false} tick={{ fill: INK_MUTED, fontSize: 11 }} allowDecimals={false}
                tickFormatter={(v: number) => (v >= 1000 ? `$${(v / 1000).toFixed(v % 1000 ? 1 : 0)}K` : `$${v}`)} />
              <Tooltip content={(props) => <ChartTooltip {...(props as TooltipContentProps<number, string>)} />} cursor={{ fill: "#f6f0ee" }} />
              <Bar dataKey="value" fill={ROSE} radius={[4, 4, 0, 0]} maxBarSize={24} isAnimationActive={false} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </Section>
  )
}
