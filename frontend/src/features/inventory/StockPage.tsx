import { useCallback, useState } from "react"
import { Link, useSearchParams } from "react-router"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowLeftRight, PackagePlus, SlidersHorizontal } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { Field, SearchInput, Segmented, SubmitButton } from "@/components/common/controls"
import { Facts, PageHeader } from "@/components/common/page"
import { Pill, StockStatusPill } from "@/components/common/StatusBadge"
import { EmptyState, ErrorState, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet"
import { Switch } from "@/components/ui/switch"
import { api, errorMessage } from "@/lib/api"
import { dateOnly, daysUntil } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useSettings, useWarehouses } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Paged, ProductStock, StockFilter, StockRow } from "@/types/api"
import { AdjustStockDialog, type AdjustTarget } from "./AdjustStockDialog"

export function StockPage() {
  const { can } = useAuth()
  const [params, setParams] = useSearchParams()
  const search = params.get("search") ?? ""
  const warehouseId = params.get("warehouse") ?? ""
  const filter = (params.get("filter") ?? "All") as StockFilter
  const page = Number(params.get("page") ?? 1)
  const productId = params.get("product")
  const { data: warehouses } = useWarehouses()
  const { data: settings } = useSettings()

  const set = useCallback((patch: Record<string, string | null>) => setParams((prev) => {
    const next = new URLSearchParams(prev)
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k) }
    if (!("page" in patch) && !("product" in patch)) next.delete("page")
    return next
  }, { replace: true }), [setParams])

  const query = { search, warehouseId: warehouseId || undefined, filter: filter === "All" ? undefined : filter, page, pageSize: 50 }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.stock(query), queryFn: () => api<Paged<StockRow>>("/api/inventory", { query }), placeholderData: keepPreviousData,
  })

  const columns: Column<StockRow>[] = [
    { key: "product", header: "Product", cell: (r) => <div><div className="font-medium">{r.name}</div><div className="code text-muted-foreground">{r.sku}</div></div> },
    { key: "onHand", header: "On hand", align: "right", cell: (r) => <span className="tabular">{r.onHand}</span> },
    { key: "reserved", header: "Reserved", align: "right", hideOnMobile: true, cell: (r) => <span className="tabular text-muted-foreground">{r.reserved || "—"}</span> },
    { key: "available", header: "Available", align: "right", cell: (r) => <span className={cn("tabular font-semibold", r.available <= 0 && "text-tone-red")}>{r.available}</span> },
    { key: "threshold", header: "Alert at", align: "right", hideOnMobile: true, cell: (r) => <span className="tabular text-muted-foreground">{r.lowStockThreshold || "—"}</span> },
    { key: "status", header: "Status", cell: (r) => <StockStatusPill status={r.status} /> },
    {
      key: "expiry", header: "Next expiry", hideOnMobile: true,
      cell: (r) => (
        <div className="flex flex-wrap items-center gap-1.5 text-xs">
          {r.expiredUnits > 0 && <Pill tone="red">{r.expiredUnits} expired</Pill>}
          {r.nearestExpiry
            ? <span className={cn("tabular", daysUntil(r.nearestExpiry) <= (settings?.expiryWarningDays ?? 60) && "text-tone-amber font-medium")}>{dateOnly(r.nearestExpiry)}</span>
            : r.expiredUnits === 0 && <span className="text-muted-foreground">—</span>}
        </div>
      ),
    },
  ]

  return (
    <>
      <PageHeader title="Inventory" description="Stock of every individual product. Bundles draw from these."
        actions={<>
          {can(P.inventory.transfer) && (warehouses?.length ?? 0) > 1 && <Button asChild variant="outline"><Link to="/inventory/transfers/new"><ArrowLeftRight /> Transfer</Link></Button>}
          {can(P.inventory.receive) && <Button asChild><Link to="/inventory/receipts/new"><PackagePlus /> Receive stock</Link></Button>}
        </>} />
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput value={search} onChange={(v) => set({ search: v })} placeholder="Search product or SKU" />
        {(warehouses?.length ?? 0) > 1 && (
          <Select value={warehouseId || "all"} onValueChange={(v) => set({ warehouse: v === "all" ? null : v })}>
            <SelectTrigger className="h-8 w-44 bg-card" aria-label="Warehouse"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All warehouses</SelectItem>
              {warehouses?.map((w) => <SelectItem key={w.id} value={w.id}>{w.code} · {w.name}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
        <Segmented label="Stock filter" value={filter} onChange={(v) => set({ filter: v === "All" ? null : v })}
          options={[{ value: "All", label: "All" }, { value: "Low", label: "Low" }, { value: "Out", label: "Out" },
            { value: "Expiring", label: "Expiring" }, { value: "Expired", label: "Expired" }]} />
      </div>
      <DataTable columns={columns} rows={data?.items} rowKey={(r) => r.productId} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={(r) => set({ product: r.productId })}
        empty={<EmptyState title={filter === "All" ? "No products" : "Nothing in this view"}>
          {filter === "All" ? "Products appear here once they exist in the catalogue." : "Good news — no product matches this filter."}</EmptyState>}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(p) => set({ page: String(p) })} />} />
      <ProductStockSheet productId={productId} onClose={() => set({ product: null })} />
    </>
  )
}

function ProductStockSheet({ productId, onClose }: { productId: string | null; onClose: () => void }) {
  const { can } = useAuth()
  const [adjust, setAdjust] = useState<AdjustTarget | null>(null)
  const { data, isLoading, error } = useQuery({
    queryKey: qk.productStock(productId ?? ""), queryFn: () => api<ProductStock>(`/api/inventory/products/${productId}`), enabled: !!productId,
  })
  return (
    <Sheet open={!!productId} onOpenChange={(o) => !o && onClose()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        {isLoading || !data ? (
          error ? <div className="p-4"><ErrorState error={error} /></div> : <div className="grid h-40 place-items-center"><InlineSpinner /></div>
        ) : (
          <>
            <SheetHeader>
              <SheetTitle className="font-display text-xl">{data.name}</SheetTitle>
              <SheetDescription className="code">{data.sku}</SheetDescription>
            </SheetHeader>
            <div className="grid gap-6 px-4 pb-6">
              <div className="grid grid-cols-3 border-y border-hairline py-3 text-center">
                {[["On hand", data.onHand], ["Reserved", data.reserved], ["Available", data.available]].map(([l, v]) => (
                  <div key={l as string}><div className="font-display text-2xl tabular">{v}</div><div className="eyebrow">{l}</div></div>
                ))}
              </div>

              {data.warehouses.length > 1 && (
                <div>
                  <p className="eyebrow mb-2">By warehouse</p>
                  <Facts items={data.warehouses.map((w) => [w.code, <span key="f127-70" className="tabular">{w.onHand} on hand · {w.available} available</span>])} />
                </div>
              )}

              <div>
                <div className="mb-2 flex items-center justify-between">
                  <p className="eyebrow">Batches</p>
                  {can(P.inventory.adjust) && data.warehouses[0] && (
                    <Button size="xs" variant="outline" onClick={() => setAdjust({ productId: data.productId, productName: data.name, tracksExpiry: data.settings.tracksExpiry, warehouseId: data.warehouses[0].warehouseId, direction: "increase" })}>
                      <SlidersHorizontal /> Add units
                    </Button>
                  )}
                </div>
                {data.batches.length === 0 ? (
                  <p className="text-sm text-muted-foreground">No stock on hand.</p>
                ) : (
                  <ul className="divide-y divide-hairline rounded-md border border-hairline">
                    {data.batches.map((b) => (
                      <li key={`${b.batchId}-${b.warehouseId}`} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                        <div>
                          <div className="code">{b.batchNumber} <span className="text-muted-foreground">· {b.warehouseCode}</span></div>
                          <div className={cn("text-xs", b.isExpired ? "text-tone-red" : b.daysToExpiry !== null && b.daysToExpiry <= 60 ? "text-tone-amber" : "text-muted-foreground")}>
                            {b.expiryDate ? `${b.isExpired ? "Expired" : "Expires"} ${dateOnly(b.expiryDate)}${!b.isExpired && b.daysToExpiry !== null ? ` · ${b.daysToExpiry} days` : ""}` : "No expiry"}
                          </div>
                        </div>
                        <div className="flex items-center gap-2">
                          <span className="tabular font-medium">{b.onHand}</span>
                          {can(P.inventory.adjust) && (
                            <Button size="xs" variant="ghost" onClick={() => setAdjust({
                              productId: data.productId, productName: data.name, tracksExpiry: data.settings.tracksExpiry, warehouseId: b.warehouseId,
                              direction: "decrease", batch: { id: b.batchId, number: b.batchNumber, onHand: b.onHand, isExpired: b.isExpired },
                            })}>Remove</Button>
                          )}
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>

              <SettingsEditor stock={data} />

              <Button asChild variant="link" className="justify-self-start px-0">
                <Link to={`/inventory/movements?product=${data.productId}`}>View movement history →</Link>
              </Button>
            </div>
            {adjust && <AdjustStockDialog target={adjust} onClose={() => setAdjust(null)} />}
          </>
        )}
      </SheetContent>
    </Sheet>
  )
}

function SettingsEditor({ stock }: { stock: ProductStock }) {
  const { can } = useAuth()
  const queryClient = useQueryClient()
  const [threshold, setThreshold] = useState(String(stock.settings.lowStockThreshold))
  const [tracksExpiry, setTracksExpiry] = useState(stock.settings.tracksExpiry)
  const editable = can(P.inventory.thresholds)
  const save = useMutation({
    mutationFn: () => api(`/api/inventory/settings/${stock.productId}`, { method: "PUT", body: { lowStockThreshold: Number(threshold), tracksExpiry } }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["inventory"] }); toast.success("Stock alert saved") },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const valid = /^\d{1,7}$/.test(threshold)
  return (
    <form className="grid gap-3 rounded-md border border-hairline p-3" onSubmit={(e) => { e.preventDefault(); if (valid) save.mutate() }}>
      <p className="eyebrow">Stock alert</p>
      <Field label="Warn when available is at or below" htmlFor="threshold" error={!valid ? "Whole number." : undefined}
        hint={editable ? undefined : "Only managers can change this."}>
        <Input id="threshold" inputMode="numeric" value={threshold} onChange={(e) => setThreshold(e.target.value)} disabled={!editable} className="w-28 tabular" />
      </Field>
      <label className="flex items-center gap-2 text-sm">
        <Switch checked={tracksExpiry} onCheckedChange={setTracksExpiry} disabled={!editable} /> Track expiry dates for this product
      </label>
      {editable && <div><SubmitButton size="sm" pending={save.isPending} disabled={!valid}>Save</SubmitButton></div>}
    </form>
  )
}
