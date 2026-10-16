import { useState, type ReactNode } from "react"
import { Link, useParams } from "react-router"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Check, Pencil, Printer, RotateCcw, Truck, Undo2, X } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { ConfirmDialog, Field, SubmitButton } from "@/components/common/controls"
import { Facts, PageHeader, Section } from "@/components/common/page"
import { OrderStatusPill, PaymentStatusPill, Pill } from "@/components/common/StatusBadge"
import { ErrorState, FullPageSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { dateTime, humanize, money } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useDeliveryCompanies } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { OrderDetail, OrderStatus } from "@/types/api"

type Action = "confirm" | "reopen" | "deliver" | "return" | "cancel" | "mark-paid"

const actionDone: Record<Action, string> = {
  confirm: "confirmed — stock deducted", reopen: "reopened as draft — stock returned", deliver: "marked delivered",
  return: "returned — stock back on the shelf", cancel: "cancelled", "mark-paid": "marked paid",
}

const actionCopy: Record<Action, { title: string; description: string; label: string; destructive?: boolean; note?: boolean }> = {
  confirm: { title: "Confirm order?", description: "Stock for every item, including products inside bundles, will be deducted immediately.", label: "Confirm & deduct stock" },
  reopen: { title: "Reopen as draft?", description: "The deducted stock is returned so the order can be edited.", label: "Reopen" },
  deliver: { title: "Mark as delivered?", description: "Cash-on-delivery orders become “with courier” until the courier hands over the cash (or paid, if your own staff delivered).", label: "Mark delivered" },
  return: { title: "Record a returned delivery?", description: "Use when the customer refused or the courier couldn't deliver. The units go back into stock, into the batches they left from.", label: "Record return", destructive: true, note: true },
  cancel: { title: "Cancel this order?", description: "Any stock deducted at confirmation is returned. This cannot be undone.", label: "Cancel order", destructive: true, note: true },
  "mark-paid": { title: "Record payment?", description: "Confirms the customer has paid this order directly.", label: "Mark paid", note: true },
}

export function OrderDetailPage() {
  const { id = "" } = useParams()
  const { can } = useAuth()
  const queryClient = useQueryClient()
  const { data: o, isLoading, error, refetch } = useQuery({ queryKey: qk.order(id), queryFn: () => api<OrderDetail>(`/api/orders/${id}`) })
  const [action, setAction] = useState<Action | null>(null)
  const [note, setNote] = useState("")
  const [shipping, setShipping] = useState(false)
  const [editingDelivery, setEditingDelivery] = useState(false)

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

  if (isLoading) return <FullPageSpinner />
  if (error || !o) return <ErrorState error={error} onRetry={refetch} />

  const allowed = (s: OrderStatus) => o.allowedTransitions.includes(s)
  const buttons: ReactNode[] = []
  if (o.status === "Draft" && can(P.orders.update)) buttons.push(<Button key="edit" variant="outline" asChild><Link to={`/orders/${o.id}/edit`}><Pencil /> Edit</Link></Button>)
  if (allowed("Cancelled") && can(P.orders.cancel)) buttons.push(<Button key="cancel" variant="ghost" onClick={() => setAction("cancel")}><X /> Cancel</Button>)
  if (allowed("Draft") && can(P.orders.update)) buttons.push(<Button key="reopen" variant="outline" onClick={() => setAction("reopen")}><Undo2 /> Reopen</Button>)
  if (allowed("Returned") && can(P.orders.update)) buttons.push(<Button key="return" variant="outline" onClick={() => setAction("return")}><RotateCcw /> Returned</Button>)
  if (o.paymentMethod !== "CashOnDelivery" && o.paymentStatus === "Unpaid" && !["Cancelled", "Returned"].includes(o.status) && can(P.orders.payments))
    buttons.push(<Button key="paid" variant="outline" onClick={() => setAction("mark-paid")}>Mark paid</Button>)
  if (allowed("Confirmed") && can(P.orders.update)) buttons.push(<Button key="confirm" onClick={() => setAction("confirm")}><Check /> Confirm & deduct stock</Button>)
  if (allowed("Shipped") && can(P.orders.update)) buttons.push(<Button key="ship" onClick={() => setShipping(true)}><Truck /> Shipping</Button>)
  if (allowed("Delivered") && can(P.orders.update)) buttons.push(<Button key="deliver" onClick={() => setAction("deliver")}><Check /> Delivered</Button>)

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
              {o.history.map((h, idx) => (
                <li key={idx} className="relative flex gap-3 pb-4 last:pb-0">
                  <span className={cn("relative z-10 mt-1.5 size-2 shrink-0 rounded-full", h.eventType === "Payment" ? "bg-tone-green" : "bg-rose")} />
                  {idx < o.history.length - 1 && <span className="absolute top-3 left-[3.5px] h-full w-px bg-hairline" />}
                  <div className="text-sm">
                    <div>
                      {h.eventType === "Payment" ? "Payment " : ""}
                      {h.oldValue ? <>{humanize(h.oldValue)} → <span className="font-medium">{humanize(h.newValue)}</span></> : <span className="font-medium">Created as {h.newValue.toLowerCase()}</span>}
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
            <Button size="xs" variant="ghost" onClick={() => setEditingDelivery(true)}>Edit</Button>} bodyClassName="p-4">
            <Facts items={[
              ["Name", o.customerId ? <Link key="customer" to={`/customers?search=${encodeURIComponent(o.recipientPhone)}`} className="font-medium text-primary hover:underline">{o.recipientName}</Link> : o.recipientName],
              ["Phone", <a key="f145-26" className="hover:underline" href={`https://wa.me/${o.recipientPhone.replace(/\D/g, "")}`} target="_blank" rel="noreferrer">{o.recipientPhone}</a>],
              ["City", o.recipientCity], ["Address", o.recipientAddress],
            ]} />
          </Section>
          <Section title="Delivery & payment" bodyClassName="p-4">
            <Facts items={[
              ["Ships from", o.warehouseName],
              ["Courier", o.deliveryCompanyName ?? "Own delivery"],
              ["Waybill", o.trackingNumber ? <span key="f153-47" className="code">{o.trackingNumber}</span> : null],
              ["Courier fee", o.courierFee !== null ? money(o.courierFee) : null],
              ["Payment", humanize(o.paymentMethod)],
              ["Remittance", o.courierRemittanceNumber ? <span key="f156-59" className="code">{o.courierRemittanceNumber}</span> : null],
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
      {shipping && <ShipDialog order={o} onClose={() => setShipping(false)} onDone={(u) => afterChange(u, `${u.orderNumber} marked as shipped`)} />}
      {editingDelivery && <DeliveryDialog order={o} onClose={() => setEditingDelivery(false)} onDone={(u) => afterChange(u, "Delivery details saved")} />}
    </>
  )
}

function ShipDialog({ order, onClose, onDone }: { order: OrderDetail; onClose: () => void; onDone: (o: OrderDetail) => Promise<void> }) {
  const { data: couriers } = useDeliveryCompanies()
  const [courierId, setCourierId] = useState(order.deliveryCompanyId ?? "")
  const [tracking, setTracking] = useState(order.trackingNumber ?? "")
  const [fee, setFee] = useState(order.courierFee?.toFixed(2) ?? "")
  const feeError = fee && !/^\d{1,7}(\.\d{1,2})?$/.test(fee) ? "Invalid amount." : undefined
  const ship = useMutation({
    mutationFn: () => api<OrderDetail>(`/api/orders/${order.id}/ship`, {
      method: "POST", body: { deliveryCompanyId: courierId || null, trackingNumber: tracking.trim() || null, courierFee: fee ? Number(fee) : null, notes: null },
    }),
    onSuccess: async (u) => { await onDone(u); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Shipping for {order.orderNumber}</DialogTitle>
          <DialogDescription>Stock was already deducted when the order was confirmed. Add the courier and tracking details here.</DialogDescription>
        </DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (!feeError) ship.mutate() }}>
          <Field label="Delivery company" htmlFor="s-courier">
            <Select value={courierId || "none"} onValueChange={(v) => setCourierId(v === "none" ? "" : v)}>
              <SelectTrigger id="s-courier" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="none">Own delivery (ÀLara Line staff)</SelectItem>
                {couriers?.map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </Field>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Waybill / tracking no." htmlFor="s-track"><Input id="s-track" className="font-mono" value={tracking} onChange={(e) => setTracking(e.target.value)} /></Field>
            <Field label="Courier's fee (USD)" htmlFor="s-fee" error={feeError} hint="What they keep from the cash."><Input id="s-fee" inputMode="decimal" className="tabular" value={fee} onChange={(e) => setFee(e.target.value)} /></Field>
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={ship.isPending}><Truck /> Ship</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function DeliveryDialog({ order, onClose, onDone }: { order: OrderDetail; onClose: () => void; onDone: (o: OrderDetail) => Promise<void> }) {
  const { data: couriers } = useDeliveryCompanies()
  const [name, setName] = useState(order.recipientName)
  const [phone, setPhone] = useState(order.recipientPhone)
  const [city, setCity] = useState(order.recipientCity ?? "")
  const [address, setAddress] = useState(order.recipientAddress ?? "")
  const [courierId, setCourierId] = useState(order.deliveryCompanyId ?? "")
  const [tracking, setTracking] = useState(order.trackingNumber ?? "")
  const [fee, setFee] = useState(order.courierFee?.toFixed(2) ?? "")
  const save = useMutation({
    mutationFn: () => api<OrderDetail>(`/api/orders/${order.id}/delivery`, {
      method: "PUT",
      body: {
        recipient: { name, phone, city: city || null, address: address || null }, deliveryCompanyId: courierId || null,
        trackingNumber: tracking || null, courierFee: fee ? Number(fee) : null, notes: order.notes,
      },
    }),
    onSuccess: async (u) => { await onDone(u); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>Customer & delivery</DialogTitle></DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Name" htmlFor="d-name" required><Input id="d-name" value={name} onChange={(e) => setName(e.target.value)} /></Field>
            <Field label="Phone" htmlFor="d-phone" required><Input id="d-phone" value={phone} onChange={(e) => setPhone(e.target.value)} /></Field>
            <Field label="City" htmlFor="d-city"><Input id="d-city" value={city} onChange={(e) => setCity(e.target.value)} /></Field>
            <Field label="Courier" htmlFor="d-courier">
              <Select value={courierId || "none"} onValueChange={(v) => setCourierId(v === "none" ? "" : v)}>
                <SelectTrigger id="d-courier" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Own delivery</SelectItem>
                  {couriers?.map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
          </div>
          <Field label="Address" htmlFor="d-address"><Textarea id="d-address" rows={2} value={address} onChange={(e) => setAddress(e.target.value)} /></Field>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Waybill" htmlFor="d-track"><Input id="d-track" className="font-mono" value={tracking} onChange={(e) => setTracking(e.target.value)} /></Field>
            <Field label="Courier fee (USD)" htmlFor="d-fee"><Input id="d-fee" inputMode="decimal" value={fee} onChange={(e) => setFee(e.target.value)} /></Field>
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} disabled={!name.trim() || !phone.trim()}>Save</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
