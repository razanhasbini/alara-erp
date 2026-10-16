import { useState, type ReactNode } from "react"
import { Link, useParams } from "react-router"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { AlarmClock, Check, Clock, Pencil, Printer, RotateCcw, Truck, Undo2, X } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { ConfirmDialog, Field, SubmitButton } from "@/components/common/controls"
import { Facts, PageHeader, Section } from "@/components/common/page"
import { OrderStatusPill, PaymentStatusPill, Pill } from "@/components/common/StatusBadge"
import { ErrorState, FullPageSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { dateTime, humanize, money } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { OrderDetail, OrderStatus } from "@/types/api"
import { ReserveTimeDialog } from "./ReserveTimeDialog"

type Action = "confirm" | "ship" | "deliver" | "return" | "cancel"
type ReserveMode = "reserve" | "change"

const actionDone: Record<Action, string> = {
  confirm: "sold — paid and stock deducted", ship: "marked as shipped", deliver: "marked delivered",
  return: "returned — stock back on the shelf", cancel: "cancelled",
}

const actionCopy: Record<Action, { title: string; description: string; label: string; destructive?: boolean; note?: boolean }> = {
  confirm: { title: "Mark as sold?", description: "The held stock for every item, including products inside bundles, is deducted and the sale is counted in revenue and profit.", label: "Mark as sold" },
  ship: { title: "Mark as shipped?", description: "The order has left ÀLara Line. Stock and payment were already applied when it was sold.", label: "Mark shipped", note: true },
  deliver: { title: "Mark as delivered?", description: "The customer has received the order. Payment was already recorded when it was sold.", label: "Mark delivered" },
  return: { title: "Record a returned delivery?", description: "Use when the customer refused or delivery failed. The units go back into stock, into the batches they left from.", label: "Record return", destructive: true, note: true },
  cancel: { title: "Cancel this order?", description: "Reserved stock is released and any stock deducted when it was sold is returned. This cannot be undone.", label: "Cancel order", destructive: true, note: true },
}

export function OrderDetailPage() {
  const { id = "" } = useParams()
  const { can } = useAuth()
  const queryClient = useQueryClient()
  const { data: o, isLoading, error, refetch } = useQuery({ queryKey: qk.order(id), queryFn: () => api<OrderDetail>(`/api/orders/${id}`) })
  const [action, setAction] = useState<Action | null>(null)
  const [note, setNote] = useState("")
  const [editingRecipient, setEditingRecipient] = useState(false)
  const [reserveMode, setReserveMode] = useState<ReserveMode | null>(null)

  const afterChange = async (updated: OrderDetail, message: string) => {
    queryClient.setQueryData(qk.order(id), updated)
    await Promise.all(["sales", "inventory", "dashboard"].map((k) => queryClient.invalidateQueries({ queryKey: [k] })))
    toast.success(message)
  }
  const run = useMutation({
    mutationFn: ({ a, notes }: { a: Action; notes: string }) => api<OrderDetail>(`/api/orders/${id}/${a}`, { method: "POST", body: { notes: notes || null } }),
    onSuccess: async (updated, { a }) => { setAction(null); setNote(""); await afterChange(updated, `${updated.orderNumber} ${actionDone[a]}`) },
    onError: (e) => { setAction(null); toast.error(errorMessage(e)) },
  })
  const reserve = useMutation({
    mutationFn: ({ mode, until, notes }: { mode: ReserveMode; until: string; notes: string }) => mode === "reserve"
      ? api<OrderDetail>(`/api/orders/${id}/reserve`, { method: "POST", body: { reservedUntil: until, notes: notes || null } })
      : api<OrderDetail>(`/api/orders/${id}/reservation`, { method: "PUT", body: { reservedUntil: until, notes: notes || null } }),
    onSuccess: async (updated, { mode }) => {
      setReserveMode(null)
      await afterChange(updated, mode === "reserve"
        ? `${updated.orderNumber} moved back to reserved — the sale is undone and the stock is held until ${dateTime(updated.reservedUntil)}`
        : `Reservation now ends ${dateTime(updated.reservedUntil)}`)
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (isLoading) return <FullPageSpinner />
  if (error || !o) return <ErrorState error={error} onRetry={refetch} />

  const allowed = (s: OrderStatus) => o.allowedTransitions.includes(s)
  const buttons: ReactNode[] = []
  if ((o.status === "Reserved" || o.status === "Draft") && can(P.orders.update)) buttons.push(<Button key="edit" variant="outline" asChild><Link to={`/orders/${o.id}/edit`}><Pencil /> Edit</Link></Button>)
  if (allowed("Cancelled") && can(P.orders.cancel)) buttons.push(<Button key="cancel" variant="ghost" onClick={() => setAction("cancel")}><X /> Cancel</Button>)
  if (o.status === "Reserved" && can(P.orders.update)) buttons.push(<Button key="change" variant="outline" onClick={() => setReserveMode("change")}><Clock /> Change time</Button>)
  if (allowed("Reserved") && can(P.orders.update)) buttons.push(<Button key="reserve" variant="outline" onClick={() => setReserveMode("reserve")}><Undo2 /> {o.status === "Draft" ? "Reserve" : "Back to reserved"}</Button>)
  if (allowed("Returned") && can(P.orders.update)) buttons.push(<Button key="return" variant="outline" onClick={() => setAction("return")}><RotateCcw /> Returned</Button>)
  if (allowed("Confirmed") && can(P.orders.update)) buttons.push(<Button key="confirm" onClick={() => setAction("confirm")}><Check /> Mark as sold</Button>)
  if (allowed("Shipped") && can(P.orders.update)) buttons.push(<Button key="ship" onClick={() => setAction("ship")}><Truck /> Mark shipped</Button>)
  if (allowed("Delivered") && can(P.orders.update)) buttons.push(<Button key="deliver" onClick={() => setAction("deliver")}><Check /> Delivered</Button>)

  // Every order starts as an internal draft and immediately becomes reserved or sold; "Reserved until…" already
  // says it was reserved, so only the "→ Sold" half of that first step is worth showing.
  const history = o.history.filter((h) => !(h.eventType === "Status" && h.oldValue === "Draft" && h.newValue === "Reserved"))
    .map((h) => h.eventType === "Status" && h.oldValue === "Draft" ? { ...h, oldValue: null, newValue: "Sold" } : h)
  const copy = action ? actionCopy[action] : null
  return (
    <>
      <PageHeader back={{ to: "/orders", label: "Orders" }}
        title={<span className="code text-[1.7rem] font-semibold tracking-tight">{o.orderNumber}</span>}
        meta={<>
          <OrderStatusPill status={o.status} /><PaymentStatusPill status={o.paymentStatus} />
          <span className="text-xs text-muted-foreground">{o.source} · created {dateTime(o.createdAt)} by {o.createdBy ?? "—"}</span>
        </>}
        actions={<>{buttons}<Button variant="ghost" size="icon" aria-label="Print" onClick={() => window.print()}><Printer /></Button></>} />

      {o.status === "Reserved" && <ReservationBanner order={o} />}

      <div className="grid gap-6 lg:grid-cols-[1fr_21rem]">
        <div className="grid content-start gap-6">
          <Section title="Items">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead><tr className="border-b border-hairline">
                  <th className="eyebrow px-4 py-2 text-left">Product</th><th className="eyebrow px-4 py-2 text-right">Qty</th>
                  <th className="eyebrow px-4 py-2 text-right">Price</th><th className="eyebrow px-4 py-2 text-right">Discount</th><th className="eyebrow px-4 py-2 text-right">Total</th>
                </tr></thead>
                <tbody>
                  {o.items.map((i) => (
                    <tr key={i.id} className="border-b border-hairline align-top last:border-0">
                      <td className="px-4 py-2.5">
                        <div className="flex items-center gap-2"><Link to={`/products/${i.productId}`} className="hover:underline">{i.productName}</Link>{i.isGift && <Pill tone="green" dot={false}>Gift</Pill>}</div>
                        {i.components.length > 0 && (
                          <ul className="mt-1.5 border-l border-petal pl-2.5 text-xs text-muted-foreground">
                            {i.components.map((c) => <li key={c.productId}>{c.totalQuantity} × {c.name}</li>)}
                          </ul>
                        )}
                      </td>
                      <td className="px-4 py-2.5 text-right tabular">{i.quantity}</td>
                      <td className="px-4 py-2.5 text-right tabular">{i.isGift ? "Free" : money(i.unitPrice)}</td>
                      <td className="px-4 py-2.5 text-right tabular text-muted-foreground">{i.isGift ? "—" : i.lineDiscount ? `−${money(i.lineDiscount)}` : "—"}</td>
                      <td className="px-4 py-2.5 text-right tabular font-medium">{i.isGift ? "Gift" : money(i.lineTotal)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <dl className="ml-auto grid max-w-xs grid-cols-2 gap-y-1 border-t border-hairline px-4 py-3 text-sm">
              <dt className="text-muted-foreground">Items</dt><dd className="text-right tabular">{money(o.subtotal)}</dd>
              {o.discountTotal > 0 && <><dt className="text-muted-foreground">Discount</dt><dd className="text-right tabular">−{money(o.discountTotal)}</dd></>}
              <dt className="text-muted-foreground">Delivery</dt><dd className="text-right tabular">{money(o.shippingTotal)}</dd>
              <dt className="mt-1 border-t border-hairline pt-2 font-medium">Total</dt>
              <dd className="font-display mt-1 border-t border-hairline pt-1 text-right text-xl tabular">{money(o.grandTotal, o.currencyCode)}</dd>
            </dl>
          </Section>

          <Section title="History">
            <ol className="px-4 py-3">
              {history.map((h, idx) => (
                <li key={idx} className="relative flex gap-3 pb-4 last:pb-0">
                  <span className={cn("relative z-10 mt-1.5 size-2 shrink-0 rounded-full", h.eventType === "Payment" ? "bg-tone-green" : h.eventType === "Reservation" ? "bg-tone-amber" : "bg-rose")} />
                  {idx < history.length - 1 && <span className="absolute top-3 left-[3.5px] h-full w-px bg-hairline" />}
                  <div className="text-sm">
                    <div>
                      {h.eventType === "Reservation"
                        ? h.oldValue
                          ? <>Reservation time changed from {dateTime(h.oldValue)} to <span className="font-medium">{dateTime(h.newValue)}</span></>
                          : <>Reserved until <span className="font-medium">{dateTime(h.newValue)}</span></>
                        : <>
                          {h.eventType === "Payment" ? "Payment " : ""}
                          {h.oldValue ? <>{statusLabel(h.oldValue)} → <span className="font-medium">{statusLabel(h.newValue)}</span></> : <span className="font-medium">{h.newValue === "Draft" ? "Order created" : statusLabel(h.newValue)}</span>}
                        </>}
                    </div>
                    <div className="text-xs text-muted-foreground">{dateTime(h.changedAt)} · {h.changedBy ?? "System"}</div>
                    {h.notes && <div className="mt-0.5 text-xs">“{h.notes}”</div>}
                  </div>
                </li>
              ))}
            </ol>
          </Section>
        </div>

        <div className="grid content-start gap-6">
          <Section title="Customer" actions={!["Delivered", "Cancelled", "Returned"].includes(o.status) && can(P.orders.update) &&
            <Button size="xs" variant="ghost" onClick={() => setEditingRecipient(true)}>Edit</Button>} bodyClassName="p-4">
            <Facts items={[
              ["Name", o.customerId ? <Link key="customer" to={`/customers?search=${encodeURIComponent(o.recipientPhone)}`} className="font-medium text-primary hover:underline">{o.recipientName}</Link> : o.recipientName],
              ["Phone", <a key="f145-26" className="hover:underline" href={`https://wa.me/${o.recipientPhone.replace(/\D/g, "")}`} target="_blank" rel="noreferrer">{o.recipientPhone}</a>],
              ["City", o.recipientCity], ["Address", o.recipientAddress],
            ]} />
          </Section>
          <Section title="Order & payment" bodyClassName="p-4">
            <Facts items={[
              ["Ships from", o.warehouseName],
              ["Payment", humanize(o.paymentMethod)],
              ["Paid", o.paidAt ? dateTime(o.paidAt) : null],
            ]} />
          </Section>
          {o.notes && <Section title="Notes" bodyClassName="p-4 text-sm whitespace-pre-line">{o.notes}</Section>}
        </div>
      </div>

      {copy && (
        <ConfirmDialog open onOpenChange={(open) => !open && setAction(null)} title={copy.title} description={copy.description}
          confirmLabel={copy.label} destructive={copy.destructive} pending={run.isPending} onConfirm={() => run.mutate({ a: action!, notes: note })}>
          {copy.note && <Textarea aria-label="Note" placeholder="Note (optional)" rows={2} value={note} onChange={(e) => setNote(e.target.value)} />}
        </ConfirmDialog>
      )}
      {reserveMode && (
        <ReserveTimeDialog open onOpenChange={(open) => !open && setReserveMode(null)} pending={reserve.isPending}
          initial={reserveMode === "change" ? o.reservedUntil : null}
          title={reserveMode === "change" ? "Change reservation time" : o.status === "Draft" ? "Reserve stock for this order" : "Move back to reserved?"}
          description={reserveMode === "change"
            ? "Choose a new time. You get a new alert when it's up."
            : o.status === "Draft" ? "The products and gifts are held for this customer until the time you choose."
            : "The sale is undone: the deducted stock is returned and held again for this customer, and revenue and profit stop counting it. You can then edit the order."}
          confirmLabel={reserveMode === "change" ? "Save time" : "Reserve"}
          onConfirm={(until, notes) => reserve.mutate({ mode: reserveMode, until, notes: notes.trim() })} />
      )}
      {editingRecipient && <RecipientDialog order={o} onClose={() => setEditingRecipient(false)} onDone={(u) => afterChange(u, "Customer details saved")} />}
    </>
  )
}

/** Confirmed orders are shown to staff as "Sold". */
const statusLabel = (value: string) => value === "Confirmed" ? "Sold" : humanize(value)

function ReservationBanner({ order }: { order: OrderDetail }) {
  const expired = !!order.reservedUntil && new Date(order.reservedUntil).getTime() <= Date.now()
  return (
    <div role="status" className={cn("mb-6 flex items-start gap-3 rounded-lg border px-4 py-3 text-sm",
      expired ? "border-tone-red/30 bg-tone-red-bg text-tone-red" : "border-tone-amber/30 bg-tone-amber-bg text-tone-amber")}>
      {expired ? <AlarmClock className="mt-0.5 size-5 shrink-0" aria-hidden /> : <Clock className="mt-0.5 size-5 shrink-0" aria-hidden />}
      <div>
        <p className="font-semibold">{expired ? `Reservation time is up (${dateTime(order.reservedUntil)})` : `Reserved until ${dateTime(order.reservedUntil)}`}</p>
        <p className="mt-0.5 leading-5">
          {expired
            ? "The stock is still held. Mark the order as sold, give it more time, or cancel it to release the stock."
            : "Stock is held for this customer and can't be sold to anyone else. Nothing is counted as sold, and no cost is recorded, until you mark it as sold."}
        </p>
      </div>
    </div>
  )
}

function RecipientDialog({ order, onClose, onDone }: { order: OrderDetail; onClose: () => void; onDone: (o: OrderDetail) => Promise<void> }) {
  const [name, setName] = useState(order.recipientName)
  const [phone, setPhone] = useState(order.recipientPhone)
  const [city, setCity] = useState(order.recipientCity ?? "")
  const [address, setAddress] = useState(order.recipientAddress ?? "")
  const save = useMutation({
    mutationFn: () => api<OrderDetail>(`/api/orders/${order.id}/recipient`, {
      method: "PUT",
      body: { recipient: { name, phone, city: city || null, address: address || null }, notes: order.notes },
    }),
    onSuccess: async (u) => { await onDone(u); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>Customer details</DialogTitle></DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Name" htmlFor="d-name" required><Input id="d-name" value={name} onChange={(e) => setName(e.target.value)} /></Field>
            <Field label="Phone" htmlFor="d-phone" required><Input id="d-phone" value={phone} onChange={(e) => setPhone(e.target.value)} /></Field>
            <Field label="City" htmlFor="d-city"><Input id="d-city" value={city} onChange={(e) => setCity(e.target.value)} /></Field>
          </div>
          <Field label="Address" htmlFor="d-address"><Textarea id="d-address" rows={2} value={address} onChange={(e) => setAddress(e.target.value)} /></Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} disabled={!name.trim() || !phone.trim()}>Save</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
