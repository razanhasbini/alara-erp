import { useMemo, useState } from "react"
import { useNavigate, useParams } from "react-router"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Minus, Plus, X } from "lucide-react"
import { toast } from "sonner"
import { Field } from "@/components/common/controls"
import { PageHeader, Section } from "@/components/common/page"
import { ProductPicker } from "@/components/common/ProductPicker"
import { ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, ApiError, errorMessage } from "@/lib/api"
import { humanize, money } from "@/lib/format"
import { qk, useAvailability, useDeliveryCompanies, useSettings, useWarehouses } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { OrderDetail, OrderSource, PaymentMethod, ProductType } from "@/types/api"
import { SOURCES } from "@/lib/rules"

interface Line { productId: string; name: string; sku: string; type: ProductType; unitPrice: number; quantity: number; discount: string }

const moneyRe = /^\d{0,7}(\.\d{0,2})?$/

export function OrderFormPage() {
  const { id } = useParams()
  const { data: existing, isLoading, error } = useQuery({ queryKey: qk.order(id ?? ""), queryFn: () => api<OrderDetail>(`/api/orders/${id}`), enabled: !!id })
  if (id && isLoading) return <FullPageSpinner />
  if (error) return <ErrorState error={error} />
  if (existing && existing.status !== "Draft") return <ErrorState error={new Error("Only draft orders can be edited. Reopen the order first.")} />
  return <OrderEditor key={existing?.id ?? "new"} existing={existing} />
}

/** The form itself; its state starts from the order being edited (or empty for a new one). */
function OrderEditor({ existing }: { existing?: OrderDetail }) {
  const id = existing?.id
  const isEdit = !!existing
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { data: settings } = useSettings()
  const { data: warehouses } = useWarehouses()
  const { data: couriers } = useDeliveryCompanies()

  const [source, setSource] = useState<OrderSource>(existing?.source ?? "WhatsApp")
  const [name, setName] = useState(existing?.recipientName ?? "")
  const [phone, setPhone] = useState(existing?.recipientPhone ?? "")
  const [city, setCity] = useState(existing?.recipientCity ?? "")
  const [address, setAddress] = useState(existing?.recipientAddress ?? "")
  const [warehouseId, setWarehouseId] = useState(existing?.warehouseId ?? "")
  const [courierId, setCourierId] = useState(existing?.deliveryCompanyId ?? "")
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>(existing?.paymentMethod ?? "CashOnDelivery")
  const [lines, setLines] = useState<Line[]>(() => existing?.items.map((i) => ({
    productId: i.productId, name: i.productName, sku: i.sku, type: i.productType, unitPrice: i.unitPrice, quantity: i.quantity,
    discount: i.lineDiscount ? i.lineDiscount.toFixed(2) : "",
  })) ?? [])
  const [discount, setDiscount] = useState(existing?.discountTotal ? existing.discountTotal.toFixed(2) : "")
  const [shipping, setShipping] = useState<string | null>(existing ? existing.shippingTotal.toFixed(2) : null)
  const [notes, setNotes] = useState(existing?.notes ?? "")
  const [submitted, setSubmitted] = useState(false)

  const effectiveWarehouse = warehouseId || warehouses?.[0]?.id || ""
  const shippingValue = shipping ?? (settings ? settings.defaultDeliveryFee.toFixed(2) : "")
  const { data: available } = useAvailability(lines.map((l) => l.productId), effectiveWarehouse || undefined, !!effectiveWarehouse)

  const totals = useMemo(() => {
    const subtotal = lines.reduce((s, l) => s + Math.max(0, l.unitPrice * l.quantity - (Number(l.discount) || 0)), 0)
    const d = Number(discount) || 0
    const sh = Number(shippingValue) || 0
    return { subtotal, discount: d, shipping: sh, total: Math.max(0, subtotal - d + sh) }
  }, [lines, discount, shippingValue])

  const errors = {
    name: !name.trim() ? "Required." : undefined,
    phone: !/^\+?[0-9 ()\-/]{6,40}$/.test(phone.trim()) ? "Enter a valid phone number." : undefined,
    lines: lines.length === 0 ? "Add at least one product." : undefined,
    discount: !moneyRe.test(discount) ? "Invalid amount." : totals.discount > totals.subtotal ? "More than the items total." : undefined,
    shipping: !moneyRe.test(shippingValue) ? "Invalid amount." : undefined,
    lineDiscounts: lines.some((l) => !moneyRe.test(l.discount) || (Number(l.discount) || 0) > l.unitPrice * l.quantity),
  }
  const shortages = lines.filter((l) => available && l.quantity > (available[l.productId] ?? 0))
  const invalid = Object.values(errors).some(Boolean)

  const save = useMutation({
    mutationFn: (confirm: boolean) => {
      const body = {
        source, warehouseId: effectiveWarehouse, paymentMethod,
        recipient: { name: name.trim(), phone: phone.trim(), city: city.trim() || null, address: address.trim() || null },
        deliveryCompanyId: courierId || null, discountTotal: Number(discount) || 0, shippingTotal: Number(shippingValue) || 0,
        notes: notes.trim() || null,
        items: lines.map((l) => ({ productId: l.productId, quantity: l.quantity, lineDiscount: Number(l.discount) || 0 })),
      }
      return isEdit
        ? api<OrderDetail>(`/api/orders/${id}`, { method: "PUT", body }).then(async (o) =>
            confirm ? api<OrderDetail>(`/api/orders/${o.id}/confirm`, { method: "POST", body: { notes: null } }) : o)
        : api<OrderDetail>("/api/orders", { method: "POST", body: { ...body, confirm } })
    },
    onSuccess: async (o) => {
      await queryClient.invalidateQueries({ queryKey: ["sales"] })
      await queryClient.invalidateQueries({ queryKey: ["inventory"] })
      await queryClient.invalidateQueries({ queryKey: ["dashboard"] })
      toast.success(o.status === "Confirmed" ? `${o.orderNumber} confirmed — stock reserved` : `${o.orderNumber} saved as draft`)
      navigate(`/orders/${o.id}`)
    },
    onError: (e) => toast.error(e instanceof ApiError && e.code === "inventory.insufficient_stock" ? e.message : errorMessage(e)),
  })

  const submit = (confirm: boolean) => {
    setSubmitted(true)
    if (!effectiveWarehouse) return void toast.error("There is no active warehouse to ship from. Create one under Warehouses first.")
    if (!invalid) save.mutate(confirm)
  }
  const setLine = (productId: string, patch: Partial<Line>) => setLines((ls) => ls.map((l) => (l.productId === productId ? { ...l, ...patch } : l)))
  const show = (k: keyof typeof errors) => (submitted ? (errors[k] as string | undefined) : undefined)

  return (
    <>
      <PageHeader back={isEdit ? { to: `/orders/${id}`, label: existing?.orderNumber ?? "Order" } : { to: "/orders", label: "Orders" }}
        title={isEdit ? `Edit ${existing?.orderNumber}` : "New order"} />
      <form noValidate onSubmit={(e) => { e.preventDefault(); submit(true) }} className="grid gap-6 lg:grid-cols-[1fr_21rem]">
        <div className="grid content-start gap-6">
          <Section title="Customer" bodyClassName="grid gap-4 p-4 sm:grid-cols-2">
            <Field label="Name" htmlFor="o-name" error={show("name")} required><Input id="o-name" autoFocus={!isEdit} value={name} onChange={(e) => setName(e.target.value)} /></Field>
            <Field label="Phone" htmlFor="o-phone" error={show("phone")} required><Input id="o-phone" type="tel" placeholder="+961 70 123 456" value={phone} onChange={(e) => setPhone(e.target.value)} /></Field>
            <Field label="City / area" htmlFor="o-city"><Input id="o-city" value={city} onChange={(e) => setCity(e.target.value)} /></Field>
            <Field label="Channel" htmlFor="o-source">
              <Select value={source} onValueChange={(v) => setSource(v as OrderSource)}>
                <SelectTrigger id="o-source" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{SOURCES.map((s) => <SelectItem key={s} value={s}>{s}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Delivery address" htmlFor="o-address" className="sm:col-span-2"><Textarea id="o-address" rows={2} value={address} onChange={(e) => setAddress(e.target.value)} /></Field>
          </Section>

          <Section title="Products" bodyClassName="grid gap-3 p-4">
            {lines.length > 0 && (
              <div className="overflow-x-auto rounded-md border border-hairline">
                <table className="w-full text-sm">
                  <thead><tr className="border-b border-hairline bg-secondary/40">
                    <th className="eyebrow px-3 py-2 text-left">Product</th><th className="eyebrow px-3 py-2 text-center">Qty</th>
                    <th className="eyebrow px-3 py-2 text-right">Price</th><th className="eyebrow px-3 py-2 text-right">Discount</th>
                    <th className="eyebrow px-3 py-2 text-right">Line</th><th />
                  </tr></thead>
                  <tbody>
                    {lines.map((l) => {
                      const avail = available?.[l.productId]
                      const short = avail !== undefined && l.quantity > avail
                      const lineTotal = Math.max(0, l.unitPrice * l.quantity - (Number(l.discount) || 0))
                      return (
                        <tr key={l.productId} className="border-b border-hairline last:border-0">
                          <td className="px-3 py-2">
                            <div>{l.name}</div>
                            <div className={cn("text-xs", short ? "text-tone-red" : "text-muted-foreground")}>
                              <span className="code">{l.sku}</span>{l.type === "Bundle" && " · bundle"} · {avail === undefined ? "…" : `${avail} available`}
                            </div>
                          </td>
                          <td className="px-3 py-2">
                            <div className="flex items-center justify-center gap-1">
                              <Button type="button" size="icon-xs" variant="ghost" aria-label="Decrease" disabled={l.quantity <= 1} onClick={() => setLine(l.productId, { quantity: l.quantity - 1 })}><Minus /></Button>
                              <Input aria-label={`Quantity of ${l.name}`} inputMode="numeric" className="h-7 w-12 text-center tabular" value={l.quantity}
                                onChange={(e) => { const n = parseInt(e.target.value, 10); if (n > 0 && n <= 10000) setLine(l.productId, { quantity: n }) }} />
                              <Button type="button" size="icon-xs" variant="ghost" aria-label="Increase" onClick={() => setLine(l.productId, { quantity: l.quantity + 1 })}><Plus /></Button>
                            </div>
                          </td>
                          <td className="px-3 py-2 text-right tabular">{money(l.unitPrice)}</td>
                          <td className="px-3 py-2 text-right">
                            <Input aria-label={`Discount on ${l.name}`} inputMode="decimal" placeholder="0.00" value={l.discount}
                              onChange={(e) => setLine(l.productId, { discount: e.target.value })}
                              className={cn("ml-auto h-7 w-20 text-right tabular", submitted && (!moneyRe.test(l.discount) || (Number(l.discount) || 0) > l.unitPrice * l.quantity) && "border-destructive")} />
                          </td>
                          <td className="px-3 py-2 text-right tabular font-medium">{money(lineTotal)}</td>
                          <td className="px-1 py-2"><Button type="button" size="icon-xs" variant="ghost" aria-label={`Remove ${l.name}`} onClick={() => setLines((ls) => ls.filter((x) => x.productId !== l.productId))}><X /></Button></td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}
            <ProductPicker warehouseId={effectiveWarehouse} showAvailability exclude={lines.map((l) => l.productId)} placeholder="Add product or bundle…"
              onSelect={(p) => setLines((ls) => [...ls, { productId: p.id, name: p.name, sku: p.sku, type: p.type, unitPrice: p.sellingPrice, quantity: 1, discount: "" }])} />
            {show("lines") && <p role="alert" className="text-xs text-destructive">{show("lines")}</p>}
            {shortages.length > 0 && (
              <p className="rounded-md bg-tone-amber-bg px-3 py-2 text-xs text-tone-amber">
                Not enough stock for {shortages.map((s) => s.name).join(", ")}. You can save a draft, but it can't be confirmed until stock arrives.
              </p>
            )}
          </Section>

          <Section title="Notes" bodyClassName="p-4">
            <Textarea aria-label="Order notes" rows={2} placeholder="Gift wrap, preferred delivery time…" value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Section>
        </div>

        <div className="grid content-start gap-6 lg:sticky lg:top-16">
          <Section title="Delivery & payment" bodyClassName="grid gap-4 p-4">
            {(warehouses?.length ?? 0) > 1 && (
              <Field label="Ship from" htmlFor="o-wh">
                <Select value={effectiveWarehouse} onValueChange={setWarehouseId}>
                  <SelectTrigger id="o-wh" className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>{warehouses?.map((w) => <SelectItem key={w.id} value={w.id}>{w.code} · {w.name}</SelectItem>)}</SelectContent>
                </Select>
              </Field>
            )}
            <Field label="Delivery company" htmlFor="o-courier" hint={!couriers?.length ? "Add couriers under Deliveries & cash." : "Leave empty if delivered by ÀLara Line staff."}>
              <Select value={courierId || "none"} onValueChange={(v) => setCourierId(v === "none" ? "" : v)}>
                <SelectTrigger id="o-courier" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Own delivery / not decided</SelectItem>
                  {couriers?.map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Payment" htmlFor="o-pay">
              <Select value={paymentMethod} onValueChange={(v) => setPaymentMethod(v as PaymentMethod)}>
                <SelectTrigger id="o-pay" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{(settings?.enabledPaymentMethods ?? ["CashOnDelivery"]).map((m) => <SelectItem key={m} value={m}>{humanize(m)}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
          </Section>

          <div className="rounded-lg border border-hairline bg-card p-4 text-sm">
            <div className="flex justify-between py-1"><span className="text-muted-foreground">Items</span><span className="tabular">{money(totals.subtotal)}</span></div>
            <div className="flex items-center justify-between gap-3 py-1">
              <label htmlFor="o-discount" className="text-muted-foreground">Order discount</label>
              <Input id="o-discount" inputMode="decimal" placeholder="0.00" value={discount} onChange={(e) => setDiscount(e.target.value)}
                aria-invalid={!!show("discount")} className="h-7 w-24 text-right tabular" />
            </div>
            {show("discount") && <p className="text-right text-xs text-destructive">{show("discount")}</p>}
            <div className="flex items-center justify-between gap-3 py-1">
              <label htmlFor="o-shipping" className="text-muted-foreground">Delivery fee</label>
              <Input id="o-shipping" inputMode="decimal" value={shippingValue} onChange={(e) => setShipping(e.target.value)}
                aria-invalid={!!show("shipping")} className="h-7 w-24 text-right tabular" />
            </div>
            <div className="mt-2 flex items-baseline justify-between border-t border-hairline pt-3">
              <span className="font-medium">Total</span>
              <span className="font-display text-2xl tabular">{money(totals.total)}</span>
            </div>
            <p className="mt-1 text-right text-[0.7rem] text-muted-foreground">Final prices are set by the server at save time.</p>
            <div className="mt-4 grid gap-2">
              <Button type="submit" disabled={save.isPending || shortages.length > 0}>{save.isPending && <InlineSpinner />} Save & confirm</Button>
              <Button type="button" variant="outline" disabled={save.isPending} onClick={() => submit(false)}>Save as draft</Button>
            </div>
            <p className="mt-2 text-[0.7rem] text-muted-foreground">Confirming reserves the stock so nobody else can sell it.</p>
          </div>
        </div>
      </form>
    </>
  )
}
