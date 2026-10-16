import { useMemo, useRef, useState } from "react"
import { Link, useNavigate, useParams } from "react-router"
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query"
import { AlertTriangle, ChevronDown, ChevronUp, Clock, Gift, Minus, Plus, X } from "lucide-react"
import { toast } from "sonner"
import { Field } from "@/components/common/controls"
import { CustomerPicker } from "@/components/common/CustomerPicker"
import { PageHeader, Section } from "@/components/common/page"
import { ProductPicker } from "@/components/common/ProductPicker"
import { ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Pill } from "@/components/common/StatusBadge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, ApiError, errorMessage } from "@/lib/api"
import { dateOnly, dateTime, humanize, money } from "@/lib/format"
import { qk, useAvailability, useSettings, useWarehouses } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Customer, OrderDetail, OrderListItem, OrderSource, Paged, PaymentMethod, ProductType, ReservedStock } from "@/types/api"
import { ReserveTimeDialog } from "./ReserveTimeDialog"
import { SOURCES } from "@/lib/rules"

interface Line {
  key: string
  productId: string
  name: string
  type: ProductType
  unitPrice: number
  quantity: number
  discount: string
  isGift: boolean
}

const moneyRe = /^\d{0,7}(\.\d{0,2})?$/
const LOYAL_ORDER_COUNT = 2

export function OrderFormPage() {
  const { id } = useParams()
  const { data: existing, isLoading, error } = useQuery({ queryKey: qk.order(id ?? ""), queryFn: () => api<OrderDetail>(`/api/orders/${id}`), enabled: !!id })
  if (id && isLoading) return <FullPageSpinner />
  if (error) return <ErrorState error={error} />
  if (existing && existing.status !== "Reserved" && existing.status !== "Draft")
    return <ErrorState error={new Error("Only reserved orders can be edited. Move a sold order back to reserved first.")} />
  return <OrderEditor key={existing?.id ?? "new"} existing={existing} />
}

function OrderEditor({ existing }: { existing?: OrderDetail }) {
  const id = existing?.id
  const isEdit = !!existing
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const giftSection = useRef<HTMLDivElement>(null)
  const { data: settings } = useSettings()
  const { data: warehouses } = useWarehouses()

  const [source, setSource] = useState<OrderSource>(existing?.source ?? "WhatsApp")
  const [customerId, setCustomerId] = useState<string | null>(existing?.customerId ?? null)
  const [selectedCustomer, setSelectedCustomer] = useState<Customer | null>(null)
  const [name, setName] = useState(existing?.recipientName ?? "")
  const [phone, setPhone] = useState(existing?.recipientPhone ?? "")
  const [city, setCity] = useState(existing?.recipientCity ?? "")
  const [address, setAddress] = useState(existing?.recipientAddress ?? "")
  const [warehouseId, setWarehouseId] = useState(existing?.warehouseId ?? "")
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>(existing?.paymentMethod ?? "CashOnDelivery")
  const [lines, setLines] = useState<Line[]>(() => existing?.items.map((item) => ({
    key: crypto.randomUUID(), productId: item.productId, name: item.productName, type: item.productType,
    unitPrice: item.unitPrice, quantity: item.quantity, discount: item.lineDiscount ? item.lineDiscount.toFixed(2) : "", isGift: item.isGift,
  })) ?? [])
  const [discount, setDiscount] = useState(existing?.discountTotal ? existing.discountTotal.toFixed(2) : "")
  const [shipping, setShipping] = useState<string | null>(existing ? existing.shippingTotal.toFixed(2) : null)
  const [notes, setNotes] = useState(existing?.notes ?? "")
  const [submitted, setSubmitted] = useState(false)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [reserveOpen, setReserveOpen] = useState(false)
  const isReservedEdit = existing?.status === "Reserved"

  const { data: loadedCustomer } = useQuery({
    queryKey: ["sales", "customer", customerId],
    queryFn: () => api<Customer>(`/api/customers/${customerId}`),
    enabled: !!customerId && selectedCustomer?.id !== customerId,
  })
  const customer = selectedCustomer?.id === customerId ? selectedCustomer : loadedCustomer
  const isLoyal = (customer?.orderCount ?? 0) >= LOYAL_ORDER_COUNT
  const paidLines = lines.filter((line) => !line.isGift)
  const giftLines = lines.filter((line) => line.isGift)
  const effectiveWarehouse = warehouseId || warehouses?.[0]?.id || ""
  const shippingValue = shipping ?? (settings ? settings.defaultDeliveryFee.toFixed(2) : "")
  const productIds = [...new Set(lines.map((line) => line.productId))]
  const { data: freeStock } = useAvailability(productIds, effectiveWarehouse || undefined, !!effectiveWarehouse)
  // While a reserved order is edited, the units it already holds are available to it again.
  const available = useMemo(() => {
    if (!freeStock || !isReservedEdit || existing.warehouseId !== effectiveWarehouse) return freeStock
    const own = { ...freeStock }
    for (const item of existing.items) if (item.productId in own) own[item.productId] += item.quantity
    return own
  }, [freeStock, isReservedEdit, existing, effectiveWarehouse])

  const totals = useMemo(() => {
    const subtotal = paidLines.reduce((sum, line) => sum + Math.max(0, line.unitPrice * line.quantity - (Number(line.discount) || 0)), 0)
    const orderDiscount = Number(discount) || 0
    const delivery = Number(shippingValue) || 0
    return { subtotal, discount: orderDiscount, shipping: delivery, total: Math.max(0, subtotal - orderDiscount + delivery) }
  }, [paidLines, discount, shippingValue])

  const required = lines.reduce<Record<string, number>>((all, line) => ({ ...all, [line.productId]: (all[line.productId] ?? 0) + line.quantity }), {})
  const shortages = Object.entries(required).filter(([productId, quantity]) => available && quantity > (available[productId] ?? 0))
    .map(([productId, quantity]) => ({ line: lines.find((line) => line.productId === productId)!, needed: quantity, available: available?.[productId] ?? 0 }))
    .filter((shortage) => shortage.line)
  const shortIds = shortages.map((shortage) => shortage.line.productId)
  const { data: reservedElsewhere } = useQuery({
    queryKey: ["sales", "reserved-stock", shortIds, id ?? null],
    queryFn: () => api<ReservedStock[]>("/api/orders/reserved-stock", { query: { productIds: shortIds, excludeOrderId: id } }),
    enabled: shortIds.length > 0,
  })
  const errors = {
    name: !name.trim() ? "Required." : undefined,
    phone: !/^\+?[0-9 ()\-/]{6,40}$/.test(phone.trim()) ? "Enter a valid phone number." : undefined,
    lines: paidLines.length === 0 ? "Add at least one product to sell. Gifts are added to an order." : undefined,
    discount: !moneyRe.test(discount) ? "Invalid amount." : totals.discount > totals.subtotal ? "More than the items total." : undefined,
    shipping: !moneyRe.test(shippingValue) ? "Invalid amount." : undefined,
    lineDiscounts: paidLines.some((line) => !moneyRe.test(line.discount) || (Number(line.discount) || 0) > line.unitPrice * line.quantity),
  }
  const invalid = Object.values(errors).some(Boolean)

  const save = useMutation({
    mutationFn: ({ sell, reserveUntil, reserveNotes }: { sell: boolean; reserveUntil?: string; reserveNotes?: string | null }) => {
      const body = {
        source, warehouseId: effectiveWarehouse, paymentMethod, customerId,
        recipient: { name: name.trim(), phone: phone.trim(), city: city.trim() || null, address: address.trim() || null },
        discountTotal: Number(discount) || 0, shippingTotal: Number(shippingValue) || 0,
        notes: notes.trim() || null,
        items: lines.map((line) => ({ productId: line.productId, quantity: line.quantity, lineDiscount: line.isGift ? 0 : Number(line.discount) || 0, isGift: line.isGift })),
      }
      if (!isEdit)
        return api<OrderDetail>("/api/orders", { method: "POST", body: { ...body, confirm: sell, reserveUntil: sell ? null : reserveUntil } })
      return api<OrderDetail>(`/api/orders/${id}`, { method: "PUT", body }).then((order) => {
        if (sell) return api<OrderDetail>(`/api/orders/${order.id}/confirm`, { method: "POST", body: { notes: null } })
        if (order.status !== "Reserved" && reserveUntil)
          return api<OrderDetail>(`/api/orders/${order.id}/reserve`, { method: "POST", body: { reservedUntil: reserveUntil, notes: reserveNotes ?? null } })
        return order
      })
    },
    onSuccess: async (order) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["sales"] }),
        queryClient.invalidateQueries({ queryKey: ["inventory"] }),
        queryClient.invalidateQueries({ queryKey: ["dashboard"] }),
      ])
      toast.success(order.status === "Confirmed"
        ? `${order.orderNumber} sold — products and gifts deducted from stock`
        : `${order.orderNumber} reserved${order.reservedUntil ? ` until ${dateTime(order.reservedUntil)}` : ""} — stock is held, nothing is sold yet`)
      navigate(`/orders/${order.id}`)
    },
    onError: (error) => toast.error(error instanceof ApiError && error.code === "inventory.insufficient_stock" ? error.message : errorMessage(error)),
  })

  const ready = () => {
    setSubmitted(true)
    if (!effectiveWarehouse) { toast.error("There is no active warehouse to ship from. Create one under Warehouses first."); return false }
    return !invalid
  }
  const sell = () => { if (ready()) save.mutate({ sell: true }) }
  // Editing a reservation keeps its time; a new order (or a legacy draft) asks until when to hold the stock.
  const reserve = () => { if (ready()) { if (isReservedEdit) save.mutate({ sell: false }); else setReserveOpen(true) } }
  const updateLine = (key: string, patch: Partial<Line>) => setLines((current) => current.map((line) => line.key === key ? { ...line, ...patch } : line))
  const show = (key: keyof typeof errors) => submitted ? errors[key] as string | undefined : undefined
  const revealGiftPicker = () => {
    giftSection.current?.scrollIntoView({ behavior: "smooth", block: "center" })
    window.setTimeout(() => giftSection.current?.querySelector<HTMLButtonElement>("button")?.focus(), 350)
  }

  return (
    <>
      <PageHeader back={isEdit ? { to: `/orders/${id}`, label: existing?.orderNumber ?? "Order" } : { to: "/orders", label: "Orders" }}
        title={isEdit ? `Edit ${existing?.orderNumber}` : "New order"} />
      <form noValidate onSubmit={(event) => { event.preventDefault(); sell() }} className="grid gap-6 lg:grid-cols-[1fr_21rem]">
        <div className="grid content-start gap-6">
          <Section title="Customer" bodyClassName="grid gap-4 p-4 sm:grid-cols-2">
            <div className="grid gap-2 sm:col-span-2">
              <CustomerPicker selectedId={customerId} selectedLabel={name || "Selected customer"}
                onSelect={(picked) => {
                  setCustomerId(picked.id); setSelectedCustomer(picked); setName(picked.name); setPhone(picked.phone)
                  setCity(picked.city ?? ""); setAddress(picked.address ?? "")
                }} onClear={() => { setCustomerId(null); setSelectedCustomer(null); setHistoryOpen(false) }} />
              <p className="text-xs text-muted-foreground">Choose a returning customer, or enter a new recipient below. A new customer profile is saved automatically.</p>
              {isLoyal && customer && (
                <div role="status" className="rounded-lg border border-emerald-300 bg-emerald-50 p-4 text-emerald-950">
                  <div className="flex flex-wrap items-center gap-2">
                    <Gift className="size-5" aria-hidden />
                    <p className="mr-auto font-semibold">This is a loyal customer. Do you want to send a gift?</p>
                    <Button type="button" size="sm" onClick={revealGiftPicker}><Gift /> Add gift</Button>
                    <Button type="button" size="sm" variant="outline" onClick={() => setHistoryOpen((open) => !open)}>
                      {historyOpen ? <ChevronUp /> : <ChevronDown />} {historyOpen ? "Hide previous orders" : "Show previous orders"}
                    </Button>
                  </div>
                  <p className="mt-2 text-sm">{customer.name} has placed {customer.orderCount} orders totalling {money(customer.totalSpent)}.</p>
                  {historyOpen && <CustomerOrderHistory customer={customer} />}
                </div>
              )}
            </div>
            <Field label="Name" htmlFor="o-name" error={show("name")} required><Input id="o-name" autoFocus={!isEdit} value={name} onChange={(event) => setName(event.target.value)} /></Field>
            <Field label="Phone" htmlFor="o-phone" error={show("phone")} required><Input id="o-phone" type="tel" placeholder="+961 70 123 456" value={phone} onChange={(event) => { setPhone(event.target.value); setCustomerId(null); setSelectedCustomer(null); setHistoryOpen(false) }} /></Field>
            <Field label="City / area" htmlFor="o-city"><Input id="o-city" value={city} onChange={(event) => setCity(event.target.value)} /></Field>
            <Field label="Channel" htmlFor="o-source">
              <Select value={source} onValueChange={(value) => setSource(value as OrderSource)}>
                <SelectTrigger id="o-source" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{SOURCES.map((item) => <SelectItem key={item} value={item}>{item}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Delivery address" htmlFor="o-address" className="sm:col-span-2"><Textarea id="o-address" rows={2} value={address} onChange={(event) => setAddress(event.target.value)} /></Field>
          </Section>

          <Section title="Products" bodyClassName="grid gap-3 p-4">
            <OrderLines lines={paidLines} available={available} submitted={submitted} updateLine={updateLine}
              removeLine={(key) => setLines((current) => current.filter((line) => line.key !== key))} />
            <div className="grid gap-2 sm:grid-cols-2">
              <ProductPicker type="Individual" warehouseId={effectiveWarehouse} showAvailability exclude={paidLines.map((line) => line.productId)} placeholder="Add product…"
                onSelect={(product) => setLines((current) => [...current, toLine(product, false)])} />
              <ProductPicker type="Bundle" warehouseId={effectiveWarehouse} showAvailability exclude={paidLines.map((line) => line.productId)} placeholder="Add bundle…"
                onSelect={(product) => setLines((current) => [...current, toLine(product, false)])} />
            </div>
            {show("lines") && <p role="alert" className="text-sm font-medium text-destructive">{show("lines")}</p>}
            {shortages.length > 0 && <ShortagePanel shortages={shortages} reserved={reservedElsewhere} />}
          </Section>

          <div ref={giftSection}>
            <Section title="Gift with this order" bodyClassName="grid gap-3 p-4">
              <p className="text-sm text-muted-foreground">Optional. Gift items cost the customer nothing. They are held with a reservation and deducted from stock when the order is sold.</p>
              <OrderLines lines={giftLines} available={available} submitted={submitted} updateLine={updateLine}
                removeLine={(key) => setLines((current) => current.filter((line) => line.key !== key))} />
              <div className="grid gap-2 sm:grid-cols-2">
                <ProductPicker type="Individual" warehouseId={effectiveWarehouse} showAvailability exclude={giftLines.map((line) => line.productId)} placeholder="Choose a gift product…"
                  onSelect={(product) => setLines((current) => [...current, toLine(product, true)])} />
                <ProductPicker type="Bundle" warehouseId={effectiveWarehouse} showAvailability exclude={giftLines.map((line) => line.productId)} placeholder="Choose a gift bundle…"
                  onSelect={(product) => setLines((current) => [...current, toLine(product, true)])} />
              </div>
            </Section>
          </div>

          <Section title="Notes" bodyClassName="p-4">
            <Textarea aria-label="Order notes" rows={2} placeholder="Gift wrap, preferred delivery time…" value={notes} onChange={(event) => setNotes(event.target.value)} />
          </Section>
        </div>

        <div className="grid content-start gap-6 lg:sticky lg:top-16">
          <Section title="Payment" bodyClassName="grid gap-4 p-4">
            {(warehouses?.length ?? 0) > 1 && (
              <Field label="Ship from" htmlFor="o-wh"><Select value={effectiveWarehouse} onValueChange={setWarehouseId}>
                <SelectTrigger id="o-wh" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{warehouses?.map((warehouse) => <SelectItem key={warehouse.id} value={warehouse.id}>{warehouse.code} · {warehouse.name}</SelectItem>)}</SelectContent>
              </Select></Field>
            )}
            <Field label="Payment" htmlFor="o-pay"><Select value={paymentMethod} onValueChange={(value) => setPaymentMethod(value as PaymentMethod)}>
              <SelectTrigger id="o-pay" className="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{(settings?.enabledPaymentMethods ?? ["CashOnDelivery"]).map((method) => <SelectItem key={method} value={method}>{humanize(method)}</SelectItem>)}</SelectContent>
            </Select></Field>
          </Section>

          <div className="rounded-lg border border-hairline bg-card p-4 text-sm">
            <div className="flex justify-between py-1"><span className="text-muted-foreground">Items</span><span className="tabular">{money(totals.subtotal)}</span></div>
            {giftLines.length > 0 && <div className="flex justify-between py-1 text-emerald-700"><span>Gift items</span><span>{giftLines.reduce((sum, line) => sum + line.quantity, 0)} · free</span></div>}
            <div className="flex items-center justify-between gap-3 py-1"><label htmlFor="o-discount" className="text-muted-foreground">Order discount</label>
              <Input id="o-discount" inputMode="decimal" placeholder="0.00" value={discount} onChange={(event) => setDiscount(event.target.value)} aria-invalid={!!show("discount")} className="h-7 w-24 text-right tabular" /></div>
            {show("discount") && <p className="text-right text-sm font-medium text-destructive">{show("discount")}</p>}
            <div className="flex items-center justify-between gap-3 py-1"><label htmlFor="o-shipping" className="text-muted-foreground">Delivery fee</label>
              <Input id="o-shipping" inputMode="decimal" value={shippingValue} onChange={(event) => setShipping(event.target.value)} aria-invalid={!!show("shipping")} className="h-7 w-24 text-right tabular" /></div>
            <div className="mt-2 flex items-baseline justify-between border-t border-hairline pt-3"><span className="font-medium">Total</span><span className="font-display text-2xl tabular">{money(totals.total)}</span></div>
            <p className="mt-1 text-right text-xs text-muted-foreground">Final prices are set by the server when saved.</p>
            <div className="mt-4 grid gap-2">
              <Button type="submit" disabled={save.isPending || shortages.length > 0}>{save.isPending && save.variables?.sell && <InlineSpinner />} {isReservedEdit ? "Save & mark as sold" : "Sell now"}</Button>
              <Button type="button" variant="outline" disabled={save.isPending || shortages.length > 0} onClick={reserve}>
                {save.isPending && !save.variables?.sell ? <InlineSpinner /> : <Clock />} {isReservedEdit ? "Save reservation" : "Reserve…"}
              </Button>
            </div>
            <p className="mt-2 text-xs text-muted-foreground">
              <strong className="font-medium text-foreground">Sell now</strong> deducts stock and counts the sale.{" "}
              <strong className="font-medium text-foreground">Reserve</strong> holds the stock for this customer until a time you choose, without counting a sale or cost. Mark it as sold later from Orders → Reserved.
            </p>
          </div>
        </div>
      </form>
      <ReserveTimeDialog open={reserveOpen} onOpenChange={setReserveOpen} title="Reserve stock for this order"
        description="The products and gifts are held for this customer and can't be sold to anyone else. When the time is up you get an alert. Nothing is released automatically."
        confirmLabel="Reserve" pending={save.isPending}
        onConfirm={(until, reserveNotes) => save.mutate({ sell: false, reserveUntil: until, reserveNotes: reserveNotes.trim() || null }, { onSettled: () => setReserveOpen(false) })} />
    </>
  )
}

function OrderLines({ lines, available, submitted, updateLine, removeLine }: {
  lines: Line[]
  available?: Record<string, number>
  submitted: boolean
  updateLine: (key: string, patch: Partial<Line>) => void
  removeLine: (key: string) => void
}) {
  if (lines.length === 0) return null
  return (
    <div className="overflow-x-auto rounded-md border border-hairline">
      <table className="w-full text-sm">
        <thead><tr className="border-b border-hairline bg-secondary/40">
          <th className="eyebrow px-3 py-2 text-left">Product</th><th className="eyebrow px-3 py-2 text-center">Qty</th>
          <th className="eyebrow px-3 py-2 text-right">Price</th><th className="eyebrow px-3 py-2 text-right">Discount</th><th className="eyebrow px-3 py-2 text-right">Line</th><th />
        </tr></thead>
        <tbody>{lines.map((line) => {
          const stock = available?.[line.productId]
          const short = stock !== undefined && line.quantity > stock
          const lineTotal = Math.max(0, line.unitPrice * line.quantity - (Number(line.discount) || 0))
          return <tr key={line.key} className="border-b border-hairline last:border-0">
            <td className="px-3 py-2"><div className="flex items-center gap-2"><span>{line.name}</span>{line.isGift && <Pill tone="green" dot={false}>Gift</Pill>}</div>
              <div className={cn("text-xs", short ? "font-medium text-tone-red" : "text-muted-foreground")}>{line.type === "Bundle" && "Bundle · "}{stock === undefined ? "Checking stock…" : `${stock} available`}</div></td>
            <td className="px-3 py-2"><div className="flex items-center justify-center gap-1">
              <Button type="button" size="icon-xs" variant="ghost" aria-label={`Decrease ${line.name}`} disabled={line.quantity <= 1} onClick={() => updateLine(line.key, { quantity: line.quantity - 1 })}><Minus /></Button>
              <Input aria-label={`Quantity of ${line.name}`} inputMode="numeric" className="h-7 w-12 text-center tabular" value={line.quantity} onChange={(event) => { const quantity = parseInt(event.target.value, 10); if (quantity > 0 && quantity <= 10000) updateLine(line.key, { quantity }) }} />
              <Button type="button" size="icon-xs" variant="ghost" aria-label={`Increase ${line.name}`} onClick={() => updateLine(line.key, { quantity: line.quantity + 1 })}><Plus /></Button>
            </div></td>
            <td className="px-3 py-2 text-right tabular">{line.isGift ? "Free" : money(line.unitPrice)}</td>
            <td className="px-3 py-2 text-right">{line.isGift ? <span className="text-muted-foreground">—</span> : <Input aria-label={`Discount on ${line.name}`} inputMode="decimal" placeholder="0.00" value={line.discount} onChange={(event) => updateLine(line.key, { discount: event.target.value })} className={cn("ml-auto h-7 w-20 text-right tabular", submitted && (!moneyRe.test(line.discount) || (Number(line.discount) || 0) > line.unitPrice * line.quantity) && "border-destructive")} />}</td>
            <td className="px-3 py-2 text-right tabular font-medium">{line.isGift ? "Gift" : money(lineTotal)}</td>
            <td className="px-1 py-2"><Button type="button" size="icon-xs" variant="ghost" aria-label={`Remove ${line.name}`} onClick={() => removeLine(line.key)}><X /></Button></td>
          </tr>
        })}</tbody>
      </table>
    </div>
  )
}

function ShortagePanel({ shortages, reserved }: {
  shortages: { line: Line; needed: number; available: number }[]
  reserved?: ReservedStock[]
}) {
  const anyReserved = shortages.some((shortage) => (reserved?.find((row) => row.productId === shortage.line.productId)?.reservedQuantity ?? 0) > 0)
  return (
    <div role="alert" className="flex items-start gap-3 rounded-md border border-tone-amber/30 bg-tone-amber-bg px-4 py-3 text-sm text-tone-amber">
      <AlertTriangle className="mt-0.5 size-5 shrink-0" aria-hidden />
      <div className="grid gap-2">
        <p className="font-semibold">{anyReserved ? "Not enough free stock — some is reserved for other orders" : "Not enough stock for this order"}</p>
        <ul className="grid gap-1.5 leading-5">
          {shortages.map(({ line, needed, available }) => {
            const held = reserved?.find((row) => row.productId === line.productId)
            return <li key={line.productId}>
              <span className="font-medium">{line.name}</span>: needs {needed}, {available} free.
              {held && held.reservedQuantity > 0 && <> {held.reservedQuantity} more {held.reservedQuantity === 1 ? "is" : "are"} reserved in{" "}
                {held.orders.map((order, index) => <span key={order.orderId}>{index > 0 && ", "}
                  <Link to={`/orders/${order.orderId}`} className="font-medium underline underline-offset-2">{order.orderNumber}</Link>
                  {" "}({order.quantity} for {order.recipientName}{order.reservedUntil && `, until ${dateTime(order.reservedUntil)}`})</span>)}.</>}
            </li>
          })}
        </ul>
        <p className="text-xs">{anyReserved ? "Free stock by selling, cancelling or editing one of those reservations, or receive more stock." : "Receive more stock, or lower the quantity."}</p>
      </div>
    </div>
  )
}

function CustomerOrderHistory({ customer }: { customer: Customer }) {
  const { data: orders, isLoading } = useQuery({
    queryKey: ["sales", "customer-order-history", customer.id],
    queryFn: () => api<Paged<OrderListItem>>("/api/orders", { query: { customerId: customer.id, statuses: ["Confirmed", "Shipped", "Delivered"], sort: "-created", pageSize: 10 } }),
  })
  const details = useQueries({
    queries: (orders?.items ?? []).map((order) => ({ queryKey: qk.order(order.id), queryFn: () => api<OrderDetail>(`/api/orders/${order.id}`), staleTime: 30_000 })),
  })
  if (isLoading) return <p className="mt-3 text-sm">Loading previous orders…</p>
  if (!orders?.items.length) return <p className="mt-3 text-sm">No previous confirmed orders yet.</p>
  return <ul className="mt-3 list-disc space-y-2 pl-5 text-sm">
    {orders.items.map((order, index) => {
      const detail = details[index]?.data
      if (!detail) return <li key={order.id}>Loading {order.orderNumber}…</li>
      const bought = detail.items.filter((item) => !item.isGift).map((item) => `${item.quantity} × ${item.productName}`).join(", ")
      const gifts = detail.items.filter((item) => item.isGift).map((item) => `${item.quantity} × ${item.productName}`).join(", ")
      return <li key={order.id}>{customer.name} ordered {bought || "no paid products"} on {dateOnly(order.createdAt)}.{gifts && <> Received a gift of {gifts}.</>}</li>
    })}
  </ul>
}

function toLine(product: { id: string; name: string; type: ProductType; sellingPrice: number }, isGift: boolean): Line {
  return { key: crypto.randomUUID(), productId: product.id, name: product.name, type: product.type, unitPrice: product.sellingPrice, quantity: 1, discount: "", isGift }
}
