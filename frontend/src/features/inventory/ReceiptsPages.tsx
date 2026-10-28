import { useState } from "react"
import { Link, useNavigate, useParams, useSearchParams } from "react-router"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { AlertTriangle, PackagePlus, X } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { Field, SearchInput, SubmitButton } from "@/components/common/controls"
import { Facts, PageHeader, Section } from "@/components/common/page"
import { ProductPicker } from "@/components/common/ProductPicker"
import { EmptyState, ErrorState, FullPageSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, ApiError, errorMessage } from "@/lib/api"
import { dateOnly, dateTime, money, number, todayIso } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useSettings, useWarehouses } from "@/lib/queries"
import type { InventorySettings, Paged, ProductDetail, Receipt, ReceiptSummary } from "@/types/api"
import { BoxesLeft } from "@/features/boxes/BoxesLeft"
import { useLogView } from "@/lib/activity"

export function ReceiptsPage() {
  useLogView("Receipts")
  const { can } = useAuth()
  const { data: settings } = useSettings()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const search = params.get("search") ?? ""
  const page = Number(params.get("page") ?? 1)
  const query = { search, page, pageSize: 25 }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.receipts(query), queryFn: () => api<Paged<ReceiptSummary>>("/api/inventory/receipts", { query }), placeholderData: keepPreviousData,
  })
  const columns: Column<ReceiptSummary>[] = [
    { key: "number", header: "Receipt", cell: (r) => <span className="code font-medium">{r.receiptNumber}</span> },
    { key: "date", header: "Received", cell: (r) => dateTime(r.receivedAt) },
    { key: "supplier", header: "Supplier", cell: (r) => <div>{r.supplierName ?? "—"}{r.supplierReference && <div className="code text-muted-foreground">{r.supplierReference}</div>}</div> },
    { key: "wh", header: "Warehouse", hideOnMobile: true, cell: (r) => <span className="code">{r.warehouseCode}</span> },
    { key: "lines", header: "Lines", align: "right", hideOnMobile: true, cell: (r) => r.lineCount },
    { key: "units", header: "Units", align: "right", cell: (r) => <span className="tabular font-medium">{number(r.totalUnits)}</span> },
    { key: "by", header: "By", hideOnMobile: true, cell: (r) => <span className="text-muted-foreground">{r.performedBy ?? "—"}</span> },
  ]
  return (
    <>
      <PageHeader title="Receipts" description={settings?.expiryDatesEnabled ? "Deliveries received from suppliers, with batch, expiry and cost." : "Deliveries received from suppliers, with batch and cost."}
        actions={can(P.inventory.receive) && <Button asChild><Link to="/inventory/receipts/new"><PackagePlus /> Receive stock</Link></Button>} />
      <div className="mb-3"><SearchInput value={search} onChange={(v) => setParams(v ? { search: v } : {}, { replace: true })} placeholder="Receipt no., supplier, reference" /></div>
      <DataTable columns={columns} rows={data?.items} rowKey={(r) => r.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={(r) => navigate(`/inventory/receipts/${r.id}`)}
        empty={<EmptyState title="No receipts yet">When stock arrives from a supplier, record it with “Receive stock”.</EmptyState>}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount}
          onPage={(p) => setParams((prev) => { const n = new URLSearchParams(prev); n.set("page", String(p)); return n })} />} />
    </>
  )
}

export function ReceiptDetailPage() {
  const { id = "" } = useParams()
  const showCost = useAuth().can(P.costs.view)
  const { data: settings } = useSettings()
  const { data: r, isLoading, error } = useQuery({ queryKey: qk.receipt(id), queryFn: () => api<Receipt>(`/api/inventory/receipts/${id}`) })
  if (isLoading) return <FullPageSpinner />
  if (error || !r) return <ErrorState error={error} />
  return (
    <>
      <PageHeader back={{ to: "/inventory/receipts", label: "Receipts" }} title={<span>Receipt <span className="code text-2xl">{r.receiptNumber}</span></span>} />
      <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <Section title={`${r.lines.length} line(s) · ${number(r.totalUnits)} units`}>
          <table className="w-full text-sm">
            <thead><tr className="border-b border-hairline"><th className="eyebrow px-4 py-2 text-left">Product</th>{settings?.expiryDatesEnabled && <th className="eyebrow px-4 py-2 text-left">Batch</th>}<th className="eyebrow px-4 py-2 text-right">Units</th>{showCost && <th className="eyebrow px-4 py-2 text-right">Unit cost</th>}</tr></thead>
            <tbody>
              {r.lines.map((l) => (
                <tr key={`${l.productId}-${l.batchId}`} className="border-b border-hairline last:border-0">
                  <td className="px-4 py-2.5"><Link to={`/products/${l.productId}`} className="hover:underline">{l.productName}</Link></td>
                  {settings?.expiryDatesEnabled && <td className="px-4 py-2.5"><span className="code">{l.batchNumber}</span><div className="text-xs text-muted-foreground">{l.expiryDate ? `Expires ${dateOnly(l.expiryDate)}` : "No expiry"}</div></td>}
                  <td className="px-4 py-2.5 text-right tabular">{l.quantity}</td>
                  {showCost && <td className="px-4 py-2.5 text-right tabular">
                    {l.unitCost !== null ? money(l.unitCost) : "—"}
                    {l.costFromSaved && <div className="text-xs text-muted-foreground" title="No cost was typed on this receipt; this is the cost the stock is valued at.">saved cost</div>}
                  </td>}
                </tr>
              ))}
            </tbody>
          </table>
        </Section>
        <Section title="Details" bodyClassName="p-4">
          <Facts items={[
            ["Warehouse", r.warehouseName], ["Received", dateTime(r.receivedAt)], ["Supplier", r.supplierName],
            ["Reference", r.supplierReference],
            ...(showCost ? [["Total cost", r.totalCost !== null ? money(r.totalCost) : <span key="f82-109" className="text-muted-foreground">Some products have no cost yet</span>]] as [string, React.ReactNode][] : []),
            ["Recorded by", r.performedBy], ["Notes", r.notes],
          ]} />
        </Section>
      </div>
    </>
  )
}

interface Line {
  key: string; productId: string; name: string; tracksExpiry: boolean; quantity: string; batchNumber: string; expiryDate: string
  /** The product's saved unit cost, used when nothing is typed for this delivery. */
  /** Whether the product has a saved cost. Costs are admin-only, so this is only known (and used) for the admin. */
  hasSavedCost: boolean
  savedCost: number | null
  /** A one-off cost for this delivery only; the product's saved cost is not changed. */
  overrideCost: boolean
  unitCost: string
}

const costRe = /^\d{1,8}(\.\d{1,4})?$/
/** Cost per unit this line will be recorded at, or null when unknown. */
/** A cost has to be typed: the product has none saved, or this delivery cost something different. */
const typesCost = (l: Line) => l.overrideCost || !l.hasSavedCost
const lineCost = (l: Line) => typesCost(l) ? (l.unitCost && costRe.test(l.unitCost) ? Number(l.unitCost) : null) : l.savedCost

export function NewReceiptPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { can } = useAuth()
  const showCost = can(P.costs.view)
  const { data: warehouses } = useWarehouses()
  const { data: globalSettings } = useSettings()
  const expiryEnabled = globalSettings?.expiryDatesEnabled ?? false
  const [warehouseId, setWarehouseId] = useState("")
  const [supplierName, setSupplierName] = useState("")
  const [supplierReference, setSupplierReference] = useState("")
  const [receivedOn, setReceivedOn] = useState(todayIso())
  const [notes, setNotes] = useState("")
  const [lines, setLines] = useState<Line[]>([])
  const [submitted, setSubmitted] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const effectiveWarehouse = warehouseId || warehouses?.[0]?.id || ""

  const addProduct = async (p: { id: string; name: string }) => {
    const [settings, detail] = await Promise.all([
      api<InventorySettings>(`/api/inventory/settings/${p.id}`).catch(() => null),
      api<ProductDetail>(`/api/products/${p.id}`).catch(() => null), // standardCost is only returned to cost-viewers
    ])
    setLines((ls) => [...ls, { key: crypto.randomUUID(), productId: p.id, name: p.name, tracksExpiry: settings?.tracksExpiry ?? true,
      quantity: "", batchNumber: "", expiryDate: "", hasSavedCost: detail?.standardCost != null, savedCost: detail?.standardCost ?? null, overrideCost: false, unitCost: "" }])
  }
  const update = (key: string, patch: Partial<Line>) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)))
  const lineErrors = (l: Line) => ({
    quantity: !/^\d+$/.test(l.quantity) || Number(l.quantity) < 1 ? "Required" : undefined,
    expiryDate: !expiryEnabled ? undefined : l.tracksExpiry && !l.expiryDate ? "Required" : l.expiryDate && l.expiryDate < todayIso() ? "In the past" : undefined,
    // Costs are admin-only: other staff record units and never see or enter a cost.
    unitCost: !showCost ? undefined
      : typesCost(l) && l.unitCost && !costRe.test(l.unitCost) ? "Invalid"
      : l.overrideCost && !l.unitCost ? "Enter the cost, or use the saved one"
      : !l.hasSavedCost && !l.unitCost ? "Required: this product has no saved cost" : undefined,
  })
  const hasErrors = lines.some((l) => Object.values(lineErrors(l)).some(Boolean))
  const totalUnits = lines.reduce((s, l) => s + (Number(l.quantity) || 0), 0)
  const totalCost = lines.every((l) => lineCost(l) !== null) ? lines.reduce((s, l) => s + lineCost(l)! * (Number(l.quantity) || 0), 0) : null

  const save = useMutation({
    mutationFn: () => api<Receipt>("/api/inventory/receipts", {
      method: "POST",
      body: {
        warehouseId: effectiveWarehouse, supplierName: supplierName || null, supplierReference: supplierReference || null,
        receivedAt: receivedOn === todayIso() ? null : new Date(`${receivedOn}T12:00:00`).toISOString(), notes: notes || null,
        lines: lines.map((l) => ({ productId: l.productId, quantity: Number(l.quantity), unitCost: showCost && typesCost(l) && l.unitCost ? Number(l.unitCost) : null,
          batchNumber: l.batchNumber.trim() || null, expiryDate: expiryEnabled && l.expiryDate ? l.expiryDate : null })),
      },
    }),
    onSuccess: async (r) => {
      setFormError(null)
      await queryClient.invalidateQueries({ queryKey: ["inventory"] })
      await queryClient.invalidateQueries({ queryKey: ["dashboard"] })
      toast.success(`${r.receiptNumber} recorded · ${r.totalUnits} units`)
      navigate(`/inventory/receipts/${r.id}`)
    },
    onError: (e) => {
      const message = e instanceof ApiError ? e.message : errorMessage(e)
      setFormError(message)
      toast.error(message)
    },
  })

  const submit = () => {
    setSubmitted(true)
    setFormError(null)
    let message: string | null = null
    if (!effectiveWarehouse) message = "There is no active warehouse. Create one under Warehouses first."
    else if (lines.length === 0) message = "Add at least one product before recording the receipt."
    else if (hasErrors) {
      const missingUnits = lines.some((l) => !!lineErrors(l).quantity)
      const missingExpiry = lines.some((l) => !!lineErrors(l).expiryDate)
      const invalidCost = lines.some((l) => !!lineErrors(l).unitCost)
      const fields = [missingUnits && "units", missingExpiry && "expiry dates", invalidCost && "unit costs"].filter(Boolean)
      message = `Complete the highlighted ${fields.join(", ")} before recording the receipt.`
    }
    if (message) {
      setFormError(message)
      toast.error(message)
      requestAnimationFrame(() => document.querySelector<HTMLElement>("[aria-invalid='true']")?.focus())
      return
    }
    save.mutate()
  }

  return (
    <>
      <PageHeader back={{ to: "/inventory/receipts", label: "Receipts" }} title="Receive stock"
        description={expiryEnabled ? "Record a delivery. Each line goes into a batch; products that track expiry need the date printed on the pack." : "Record a supplier delivery and add its units to stock."} />
      {formError && (
        <div role="alert" className="mb-4 flex items-start gap-3 rounded-lg border border-destructive/35 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          <AlertTriangle className="mt-0.5 size-5 shrink-0" aria-hidden />
          <div><p className="font-semibold">Receipt not recorded</p><p className="mt-0.5 text-foreground">{formError}</p></div>
        </div>
      )}
      <form noValidate onSubmit={(e) => { e.preventDefault(); submit() }}
        className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <Section title="Products received" bodyClassName="grid gap-3 p-4">
          {lines.length === 0 && <p className="text-sm text-muted-foreground">Add the products in this delivery.</p>}
          {lines.map((l) => {
            const err: Partial<ReturnType<typeof lineErrors>> = submitted ? lineErrors(l) : {}
            return (
              <div key={l.key} className={`grid gap-3 rounded-md border border-hairline p-3 sm:items-start ${expiryEnabled ? (showCost ? "sm:grid-cols-[1fr_5.5rem_8rem_9.5rem_9rem_auto]" : "sm:grid-cols-[1fr_5.5rem_8rem_9.5rem_auto]") : (showCost ? "sm:grid-cols-[1fr_6rem_11rem_auto]" : "sm:grid-cols-[1fr_6rem_auto]")}`}>
                <div className="min-w-0 truncate text-sm font-medium">{l.name}</div>
                <Field label="Units" htmlFor={`q-${l.key}`} error={err.quantity} required>
                  <Input id={`q-${l.key}`} inputMode="numeric" value={l.quantity} aria-invalid={!!err.quantity} onChange={(e) => update(l.key, { quantity: e.target.value })} className="tabular" />
                </Field>
                {expiryEnabled && <Field label="Batch" htmlFor={`b-${l.key}`}>
                  <Input id={`b-${l.key}`} placeholder="auto" className="font-mono" value={l.batchNumber} onChange={(e) => update(l.key, { batchNumber: e.target.value })} />
                </Field>}
                {expiryEnabled && <Field label={l.tracksExpiry ? "Expiry *" : "Expiry"} htmlFor={`e-${l.key}`} error={err.expiryDate}>
                  <Input id={`e-${l.key}`} type="date" value={l.expiryDate} aria-invalid={!!err.expiryDate} onChange={(e) => update(l.key, { expiryDate: e.target.value })} />
                </Field>}
                {showCost && <LineCost line={l} error={err.unitCost} update={(patch) => update(l.key, patch)} />}
                <Button type="button" variant="ghost" size="icon-sm" className="sm:mt-6" aria-label={`Remove ${l.name}`} onClick={() => setLines((ls) => ls.filter((x) => x.key !== l.key))}><X /></Button>
              </div>
            )
          })}
          <ProductPicker type="Individual" onSelect={addProduct} placeholder="Add product to this delivery…" />
        </Section>
        <div className="grid content-start gap-6">
          <Section title="Delivery" bodyClassName="grid gap-4 p-4">
            {(warehouses?.length ?? 0) > 1 && (
              <Field label="Warehouse" htmlFor="rc-wh">
                <Select value={effectiveWarehouse} onValueChange={setWarehouseId}>
                  <SelectTrigger id="rc-wh" className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>{warehouses?.map((w) => <SelectItem key={w.id} value={w.id}>{w.code} · {w.name}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
            )}
            <Field label="Supplier" htmlFor="rc-sup"><Input id="rc-sup" value={supplierName} onChange={(e) => setSupplierName(e.target.value)} /></Field>
            <Field label="Supplier invoice / reference" htmlFor="rc-ref"><Input id="rc-ref" className="font-mono" value={supplierReference} onChange={(e) => setSupplierReference(e.target.value)} /></Field>
            <Field label="Received on" htmlFor="rc-date"><Input id="rc-date" type="date" max={todayIso()} value={receivedOn} onChange={(e) => setReceivedOn(e.target.value)} /></Field>
            <Field label="Notes" htmlFor="rc-notes"><Textarea id="rc-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
          </Section>
          <div className="frame rounded-lg bg-card p-4 text-sm">
            <div className="flex justify-between"><span className="text-muted-foreground">Units</span><span className="tabular font-medium">{number(totalUnits)}</span></div>
            {showCost && <div className="mt-1 flex justify-between"><span className="text-muted-foreground">Cost</span><span className="tabular">{totalCost !== null && lines.length ? money(totalCost) : "—"}</span></div>}
            <SubmitButton pending={save.isPending} className="mt-4 w-full">Record receipt</SubmitButton>
          </div>
          <BoxesLeft productIds={[...new Set(lines.map((l) => l.productId))]} note="Receiving stock doesn't change box counts. Add delivered boxes under Boxes." />
        </div>
      </form>
    </>
  )
}

/**
 * The saved product cost is used without typing anything. "Different cost this time" records another cost for this
 * delivery only; the product's saved cost stays as it is (change that on the product itself).
 */
function LineCost({ line, error, update }: { line: Line; error?: string; update: (patch: Partial<Line>) => void }) {
  const id = `c-${line.key}`
  const saved = line.savedCost !== null ? money(line.savedCost) : null // only the admin gets the amount
  if (line.hasSavedCost && !line.overrideCost)
    return (
      <div className="grid gap-1.5 text-sm">
        <span className="font-medium">Unit cost</span>
        <span className="flex h-9 items-center tabular">{saved ?? "Saved cost"}{saved && <span className="ml-1.5 text-xs text-muted-foreground">saved</span>}</span>
        <button type="button" className="w-fit text-xs font-medium text-primary hover:underline" onClick={() => update({ overrideCost: true, unitCost: "" })}>
          Different cost this time
        </button>
      </div>
    )
  return (
    <Field label="Unit cost $" htmlFor={id} error={error} required={!line.hasSavedCost}
      hint={line.hasSavedCost ? <>This delivery only. <button type="button" className="font-medium text-primary hover:underline" onClick={() => update({ overrideCost: false, unitCost: "" })}>Use saved{saved && ` ${saved}`}</button></>
        : error ? undefined : "No saved cost yet. What one unit cost in this delivery."}>
      <Input id={id} inputMode="decimal" autoFocus={line.overrideCost} value={line.unitCost} aria-invalid={!!error} onChange={(e) => update({ unitCost: e.target.value })} className="tabular" />
    </Field>
  )
}
