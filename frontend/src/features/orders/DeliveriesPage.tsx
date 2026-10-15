import { useMemo, useState } from "react"
import { Link } from "react-router"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Banknote, Plus } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { Field, SubmitButton } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { ActivePill } from "@/components/common/StatusBadge"
import { EmptyState, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { date, dateTime, money, todayIso } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useDeliveryCompanies } from "@/lib/queries"
import type { DeliveryCompany, Paged, Remittance, RemittanceOrder } from "@/types/api"

export function DeliveriesPage() {
  const { can } = useAuth()
  const { data: companies, isLoading, error, refetch } = useDeliveryCompanies(true)
  const [editing, setEditing] = useState<DeliveryCompany | "new" | null>(null)
  const [remitFor, setRemitFor] = useState<DeliveryCompany | null>(null)
  const owed = companies?.reduce((s, c) => s + c.awaitingRemittanceAmount - c.awaitingRemittanceFees, 0) ?? 0

  const columns: Column<DeliveryCompany>[] = [
    { key: "name", header: "Delivery company", cell: (c) => <div><div className="font-medium">{c.name}</div>{c.phone && <div className="text-xs text-muted-foreground">{c.phone}</div>}</div> },
    { key: "orders", header: "Delivered, cash not received", align: "right", cell: (c) => <span className="tabular">{c.awaitingRemittanceOrders || "—"}</span> },
    { key: "collected", header: "Cash collected", align: "right", hideOnMobile: true, cell: (c) => <span className="tabular">{c.awaitingRemittanceAmount ? money(c.awaitingRemittanceAmount) : "—"}</span> },
    { key: "due", header: "Due to ÀLara Line", align: "right", cell: (c) => <span className="tabular font-semibold">{c.awaitingRemittanceAmount ? money(c.awaitingRemittanceAmount - c.awaitingRemittanceFees) : "—"}</span> },
    { key: "status", header: "Status", hideOnMobile: true, cell: (c) => <ActivePill active={c.isActive} /> },
    {
      key: "actions", header: "", align: "right",
      cell: (c) => (
        <div className="flex justify-end gap-1" onClick={(e) => e.stopPropagation()}>
          {can(P.orders.payments) && c.awaitingRemittanceOrders > 0 && <Button size="xs" onClick={() => setRemitFor(c)}><Banknote /> Record cash</Button>}
          {can(P.delivery.manage) && <Button size="xs" variant="ghost" onClick={() => setEditing(c)}>Edit</Button>}
        </div>
      ),
    },
  ]

  return (
    <>
      <PageHeader title="Deliveries & cash"
        description="Couriers collect cash on delivery and hand it over later, minus their fee. Record each hand-over here."
        meta={owed > 0 && <span className="text-sm">Couriers currently owe <span className="font-display text-lg tabular">{money(owed)}</span></span>}
        actions={can(P.delivery.manage) && <Button variant="outline" onClick={() => setEditing("new")}><Plus /> Add delivery company</Button>} />
      <DataTable columns={columns} rows={companies} rowKey={(c) => c.id} loading={isLoading} error={error} onRetry={refetch}
        empty={<EmptyState title="No delivery companies yet">Add the courier you work with so orders can be assigned and cash tracked.</EmptyState>} />
      {can(P.orders.payments) && <RemittanceHistory />}
      {editing && <CompanyDialog company={editing === "new" ? null : editing} onClose={() => setEditing(null)} />}
      {remitFor && <RemittanceDialog company={remitFor} onClose={() => setRemitFor(null)} />}
    </>
  )
}

function RemittanceHistory() {
  const [page, setPage] = useState(1)
  const [open, setOpen] = useState<string | null>(null)
  const query = { page, pageSize: 10 }
  const { data, isLoading, error } = useQuery({
    queryKey: qk.remittances(query), queryFn: () => api<Paged<Remittance>>("/api/courier-remittances", { query }), placeholderData: keepPreviousData,
  })
  const columns: Column<Remittance>[] = [
    { key: "no", header: "Hand-over", cell: (r) => <span className="code font-medium">{r.remittanceNumber}</span> },
    { key: "date", header: "Received", cell: (r) => date(r.receivedAt) },
    { key: "company", header: "From", cell: (r) => r.deliveryCompanyName },
    { key: "orders", header: "Orders", align: "right", cell: (r) => r.orderCount },
    { key: "collected", header: "Collected", align: "right", hideOnMobile: true, cell: (r) => <span className="tabular">{money(r.amountCollected)}</span> },
    { key: "fees", header: "Courier fees", align: "right", hideOnMobile: true, cell: (r) => <span className="tabular text-muted-foreground">−{money(r.courierFees)}</span> },
    { key: "net", header: "Received", align: "right", cell: (r) => <span className="tabular font-semibold">{money(r.netAmount)}</span> },
    { key: "by", header: "Recorded by", hideOnMobile: true, cell: (r) => <span className="text-muted-foreground">{r.recordedBy ?? "—"}</span> },
  ]
  return (
    <div className="mt-8">
      <h2 className="font-display mb-3 text-xl">Cash received from couriers</h2>
      <DataTable columns={columns} rows={data?.items} rowKey={(r) => r.id} loading={isLoading} error={error}
        onRowClick={(r) => setOpen(r.id)}
        empty={<EmptyState title="No hand-overs recorded yet" />}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={setPage} />} />
      {open && <RemittanceDetail id={open} onClose={() => setOpen(null)} />}
    </div>
  )
}

function RemittanceDetail({ id, onClose }: { id: string; onClose: () => void }) {
  const { data: r } = useQuery({ queryKey: ["sales", "remittance", id], queryFn: () => api<Remittance>(`/api/courier-remittances/${id}`) })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{r ? `${r.remittanceNumber} · ${r.deliveryCompanyName}` : "Hand-over"}</DialogTitle>
          {r && <DialogDescription>Received {dateTime(r.receivedAt)}{r.recordedBy && ` · recorded by ${r.recordedBy}`}{r.notes && ` · “${r.notes}”`}</DialogDescription>}
        </DialogHeader>
        {!r ? <InlineSpinner /> : (
          <ul className="divide-y divide-hairline rounded-md border border-hairline text-sm">
            {r.orders.map((o) => (
              <li key={o.id} className="flex justify-between px-3 py-2">
                <Link to={`/orders/${o.id}`} className="code hover:underline">{o.orderNumber}</Link>
                <span className="tabular">{money(o.grandTotal)}{o.courierFee ? <span className="text-muted-foreground"> − {money(o.courierFee)}</span> : null}</span>
              </li>
            ))}
            <li className="flex justify-between bg-secondary/40 px-3 py-2 font-medium"><span>Received</span><span className="tabular">{money(r.netAmount)}</span></li>
          </ul>
        )}
      </DialogContent>
    </Dialog>
  )
}

function RemittanceDialog({ company, onClose }: { company: DeliveryCompany; onClose: () => void }) {
  const queryClient = useQueryClient()
  const { data: orders, isLoading } = useQuery({
    queryKey: qk.awaiting(company.id), queryFn: () => api<RemittanceOrder[]>(`/api/delivery-companies/${company.id}/awaiting-remittance`),
  })
  const [selected, setSelected] = useState<Set<string> | null>(null)
  const chosen = useMemo(() => selected ?? new Set(orders?.map((o) => o.id) ?? []), [selected, orders])
  const [receivedOn, setReceivedOn] = useState(todayIso())
  const [notes, setNotes] = useState("")
  const totals = useMemo(() => {
    const picked = orders?.filter((o) => chosen.has(o.id)) ?? []
    const collected = picked.reduce((s, o) => s + o.grandTotal, 0)
    const fees = picked.reduce((s, o) => s + (o.courierFee ?? 0), 0)
    return { count: picked.length, collected, fees, net: collected - fees }
  }, [orders, chosen])
  const toggle = (id: string) => { const next = new Set(chosen); if (next.has(id)) next.delete(id); else next.add(id); setSelected(next) }

  const save = useMutation({
    mutationFn: () => api<Remittance>("/api/courier-remittances", {
      method: "POST",
      body: { deliveryCompanyId: company.id, orderIds: [...chosen], receivedAt: receivedOn === todayIso() ? null : new Date(`${receivedOn}T12:00:00`).toISOString(), notes: notes || null },
    }),
    onSuccess: async (r) => {
      await Promise.all(["sales", "dashboard"].map((k) => queryClient.invalidateQueries({ queryKey: [k] })))
      toast.success(`${r.remittanceNumber}: ${money(r.netAmount)} received from ${company.name}`)
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>Cash from {company.name}</DialogTitle>
          <DialogDescription>Tick the delivered orders this hand-over covers. They will be marked as paid.</DialogDescription>
        </DialogHeader>
        {isLoading ? <InlineSpinner /> : (
          <div className="max-h-72 overflow-y-auto rounded-md border border-hairline">
            <table className="w-full text-sm">
              <tbody>
                {orders?.map((o) => (
                  <tr key={o.id} className="border-b border-hairline last:border-0">
                    <td className="w-8 px-3 py-2"><Checkbox checked={chosen.has(o.id)} onCheckedChange={() => toggle(o.id)} aria-label={`Include ${o.orderNumber}`} /></td>
                    <td className="py-2"><span className="code">{o.orderNumber}</span> <span className="text-muted-foreground">· {o.recipientName}</span><div className="text-xs text-muted-foreground">Delivered {date(o.deliveredAt)}</div></td>
                    <td className="px-3 py-2 text-right tabular">{money(o.grandTotal)}{o.courierFee ? <div className="text-xs text-muted-foreground">fee {money(o.courierFee)}</div> : null}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <div className="grid grid-cols-2 gap-4">
          <Field label="Received on" htmlFor="rm-date"><Input id="rm-date" type="date" max={todayIso()} value={receivedOn} onChange={(e) => setReceivedOn(e.target.value)} /></Field>
          <Field label="Notes" htmlFor="rm-notes"><Textarea id="rm-notes" rows={1} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
        </div>
        <dl className="grid grid-cols-2 gap-y-1 rounded-md bg-secondary/50 px-4 py-3 text-sm">
          <dt className="text-muted-foreground">Orders</dt><dd className="text-right tabular">{totals.count}</dd>
          <dt className="text-muted-foreground">Collected from customers</dt><dd className="text-right tabular">{money(totals.collected)}</dd>
          <dt className="text-muted-foreground">Courier fees</dt><dd className="text-right tabular">−{money(totals.fees)}</dd>
          <dt className="font-medium">Cash you should receive</dt><dd className="font-display text-right text-xl tabular">{money(totals.net)}</dd>
        </dl>
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button disabled={totals.count === 0 || save.isPending} onClick={() => save.mutate()}>{save.isPending && <InlineSpinner />} Record {money(totals.net)}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function CompanyDialog({ company, onClose }: { company: DeliveryCompany | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(company?.name ?? "")
  const [phone, setPhone] = useState(company?.phone ?? "")
  const [isActive, setIsActive] = useState(company?.isActive ?? true)
  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), phone: phone.trim() || null, isActive }
      return company ? api(`/api/delivery-companies/${company.id}`, { method: "PUT", body }) : api("/api/delivery-companies", { method: "POST", body })
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["sales"] }); toast.success("Delivery company saved"); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{company ? `Edit ${company.name}` : "Add delivery company"}</DialogTitle></DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (name.trim()) save.mutate() }}>
          <Field label="Name" htmlFor="dc-name" required><Input id="dc-name" value={name} onChange={(e) => setName(e.target.value)} /></Field>
          <Field label="Phone" htmlFor="dc-phone"><Input id="dc-phone" type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} /></Field>
          <label className="flex items-center gap-2 text-sm"><Switch checked={isActive} onCheckedChange={setIsActive} /> Active</label>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} disabled={!name.trim()}>Save</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
