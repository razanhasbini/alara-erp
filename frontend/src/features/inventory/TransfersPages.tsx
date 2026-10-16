import { useState } from "react"
import { Link, useNavigate, useParams, useSearchParams } from "react-router"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowLeftRight, ArrowRight, X } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { Field, SubmitButton } from "@/components/common/controls"
import { Facts, PageHeader, Section } from "@/components/common/page"
import { ProductPicker } from "@/components/common/ProductPicker"
import { EmptyState, ErrorState, FullPageSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { dateOnly, dateTime, number } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useAvailability, useSettings, useWarehouses } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Paged, Transfer } from "@/types/api"

export function TransfersPage() {
  const { can } = useAuth()
  const { data: settings } = useSettings()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const page = Number(params.get("page") ?? 1)
  const { data: warehouses } = useWarehouses()
  const query = { page, pageSize: 25 }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.transfers(query), queryFn: () => api<Paged<Transfer>>("/api/inventory/transfers", { query }), placeholderData: keepPreviousData,
  })
  const columns: Column<Transfer>[] = [
    { key: "number", header: "Transfer", cell: (t) => <span className="code font-medium">{t.transferNumber}</span> },
    { key: "date", header: "Date", cell: (t) => dateTime(t.createdAt) },
    { key: "route", header: "Route", cell: (t) => <span className="flex items-center gap-1.5">{t.fromWarehouse} <ArrowRight className="size-3 text-muted-foreground" /> {t.toWarehouse}</span> },
    { key: "units", header: "Units", align: "right", cell: (t) => <span className="tabular font-medium">{number(t.totalUnits)}</span> },
    { key: "by", header: "By", hideOnMobile: true, cell: (t) => <span className="text-muted-foreground">{t.performedBy ?? "—"}</span> },
  ]
  return (
    <>
      <PageHeader title="Transfers" description={settings?.expiryDatesEnabled ? "Stock moved between warehouses. Earliest-expiring batches move first." : "Stock moved between warehouses."}
        actions={can(P.inventory.transfer) && (warehouses?.length ?? 0) > 1 && <Button asChild><Link to="/inventory/transfers/new"><ArrowLeftRight /> New transfer</Link></Button>} />
      <DataTable columns={columns} rows={data?.items} rowKey={(t) => t.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={(t) => navigate(`/inventory/transfers/${t.id}`)}
        empty={<EmptyState title="No transfers yet">
          {(warehouses?.length ?? 0) < 2 ? "You have one warehouse. Add another under Warehouses to move stock between locations." : "Transfers you make appear here."}
        </EmptyState>}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(p) => setParams({ page: String(p) })} />} />
    </>
  )
}

export function TransferDetailPage() {
  const { id = "" } = useParams()
  const { data: settings } = useSettings()
  const { data: t, isLoading, error } = useQuery({ queryKey: qk.transfer(id), queryFn: () => api<Transfer>(`/api/inventory/transfers/${id}`) })
  if (isLoading) return <FullPageSpinner />
  if (error || !t) return <ErrorState error={error} />
  return (
    <>
      <PageHeader back={{ to: "/inventory/transfers", label: "Transfers" }} title={<span>Transfer <span className="code text-2xl">{t.transferNumber}</span></span>} />
      <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <Section title={`${number(t.totalUnits)} units moved`}>
          <ul className="divide-y divide-hairline">
            {t.lines.map((l) => (
              <li key={`${l.productId}-${l.batchId}`} className="flex items-center justify-between gap-3 px-4 py-2.5 text-sm">
                <div><div>{l.productName}</div>{settings?.expiryDatesEnabled && <div className="text-xs text-muted-foreground">Batch {l.batchNumber}{l.expiryDate && ` · exp. ${dateOnly(l.expiryDate)}`}</div>}</div>
                <span className="tabular font-medium">{l.quantity}</span>
              </li>
            ))}
          </ul>
        </Section>
        <Section title="Details" bodyClassName="p-4">
          <Facts items={[["From", t.fromWarehouse], ["To", t.toWarehouse], ["Date", dateTime(t.createdAt)], ["By", t.performedBy], ["Notes", t.notes]]} />
        </Section>
      </div>
    </>
  )
}

interface Line { productId: string; name: string; quantity: string }

export function NewTransferPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { data: warehouses } = useWarehouses()
  const { data: settings } = useSettings()
  const [from, setFrom] = useState("")
  const [to, setTo] = useState("")
  const [notes, setNotes] = useState("")
  const [lines, setLines] = useState<Line[]>([])
  const { data: available } = useAvailability(lines.map((l) => l.productId), from || undefined, !!from)
  const errors = lines.map((l) => {
    const q = Number(l.quantity)
    if (!/^\d+$/.test(l.quantity) || q < 1) return "Enter units"
    if (available && q > (available[l.productId] ?? 0)) return `Only ${available[l.productId] ?? 0} available`
    return undefined
  })
  const valid = from && to && from !== to && lines.length > 0 && errors.every((e) => !e)

  const save = useMutation({
    mutationFn: () => api<Transfer>("/api/inventory/transfers", {
      method: "POST",
      body: { fromWarehouseId: from, toWarehouseId: to, notes: notes || null, lines: lines.map((l) => ({ productId: l.productId, quantity: Number(l.quantity), batchId: null })) },
    }),
    onSuccess: async (t) => { await queryClient.invalidateQueries({ queryKey: ["inventory"] }); toast.success(`${t.transferNumber} completed`); navigate(`/inventory/transfers/${t.id}`) },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const warehouseSelect = (id: string, value: string, onChange: (v: string) => void, exclude: string) => (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger id={id} className="w-full"><SelectValue placeholder="Choose warehouse" /></SelectTrigger>
      <SelectContent>{warehouses?.filter((w) => w.id !== exclude).map((w) => <SelectItem key={w.id} value={w.id}>{w.code} · {w.name}</SelectItem>)}</SelectContent>
    </Select>
  )

  return (
    <>
      <PageHeader back={{ to: "/inventory/transfers", label: "Transfers" }} title="New transfer"
        description={settings?.expiryDatesEnabled ? "Only unreserved stock can move. Batches are picked automatically, earliest expiry first; expired units never move automatically." : "Only unreserved stock can move. Available batches are picked automatically."} />
      <form noValidate onSubmit={(e) => { e.preventDefault(); if (valid) save.mutate() }} className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <Section title="Products" bodyClassName="grid gap-3 p-4">
          {!from && <p className="text-sm text-muted-foreground">Choose the source warehouse first.</p>}
          {lines.map((l, i) => (
            <div key={l.productId} className="flex items-start gap-3 rounded-md border border-hairline p-3">
              <div className="min-w-0 flex-1 truncate text-sm font-medium">{l.name}</div>
              <Field label={`Units · ${available?.[l.productId] ?? "…"} available`} htmlFor={`tq-${l.productId}`} error={errors[i]} className="w-44">
                <Input id={`tq-${l.productId}`} inputMode="numeric" className="tabular" value={l.quantity}
                  onChange={(e) => setLines((ls) => ls.map((x) => (x.productId === l.productId ? { ...x, quantity: e.target.value } : x)))} />
              </Field>
              <Button type="button" variant="ghost" size="icon-sm" className="mt-6" aria-label={`Remove ${l.name}`} onClick={() => setLines((ls) => ls.filter((x) => x.productId !== l.productId))}><X /></Button>
            </div>
          ))}
          {from && <ProductPicker type="Individual" warehouseId={from} showAvailability exclude={lines.map((l) => l.productId)}
            onSelect={(p) => setLines((ls) => [...ls, { productId: p.id, name: p.name, quantity: "" }])} />}
        </Section>
        <div className="grid content-start gap-6">
          <Section title="Route" bodyClassName="grid gap-4 p-4">
            <Field label="From" htmlFor="tr-from">{warehouseSelect("tr-from", from, (v) => { setFrom(v); setLines([]) }, to)}</Field>
            <Field label="To" htmlFor="tr-to">{warehouseSelect("tr-to", to, setTo, from)}</Field>
            <Field label="Notes" htmlFor="tr-notes"><Textarea id="tr-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
            <SubmitButton pending={save.isPending} disabled={!valid} className={cn("w-full")}>Move stock</SubmitButton>
          </Section>
        </div>
      </form>
    </>
  )
}
