import { useState } from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { toast } from "sonner"
import { Field, SubmitButton } from "@/components/common/controls"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { humanize } from "@/lib/format"
import { useAvailability, useWarehouses } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { AdjustmentReason, ProductDetail } from "@/types/api"

// Expiry is recorded against the actual expired component batch from Inventory; a whole bundle has no expiry.
const reasons: AdjustmentReason[] = ["SampleOrGift", "Damaged", "Lost", "Other"]

/**
 * Takes whole sets out of stock without an order (PR boxes, gifts, damaged sets).
 * There is no "bundle stock": each product inside is deducted, and the ledger notes which bundle it was for.
 */
export function WithdrawBundleDialog({ bundle, onClose }: { bundle: ProductDetail; onClose: () => void }) {
  const queryClient = useQueryClient()
  const { data: warehouses } = useWarehouses()
  const [warehouseId, setWarehouseId] = useState("")
  const effective = warehouseId || warehouses?.[0]?.id || ""
  const [quantity, setQuantity] = useState("1")
  const [reason, setReason] = useState<AdjustmentReason>("SampleOrGift")
  const [notes, setNotes] = useState("")
  const qty = /^\d+$/.test(quantity) ? Number(quantity) : 0
  const { data: available } = useAvailability(bundle.bundleItems.map((b) => b.componentProductId), effective || undefined, !!effective)
  const short = bundle.bundleItems.filter((b) => available && b.quantity * qty > (available[b.componentProductId] ?? 0))

  const save = useMutation({
    mutationFn: () => api("/api/inventory/bundle-withdrawals", {
      method: "POST", body: { warehouseId: effective, bundleProductId: bundle.id, quantity: qty, reason, notes: notes.trim() || null },
    }),
    onSuccess: async () => {
      await Promise.all(["inventory", "catalog", "dashboard"].map((k) => queryClient.invalidateQueries({ queryKey: [k] })))
      toast.success(`${qty} × ${bundle.name} taken out — products deducted`)
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const invalid = qty < 1 || short.length > 0 || (reason === "Other" && !notes.trim()) || !effective

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Take {bundle.name} out of stock</DialogTitle>
          <DialogDescription>For sets given away or damaged outside an order. Each product inside is deducted from stock.</DialogDescription>
        </DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (!invalid) save.mutate() }}>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Sets" htmlFor="wb-qty" error={qty < 1 ? "At least 1." : undefined} required>
              <Input id="wb-qty" inputMode="numeric" className="tabular" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
            </Field>
            <Field label="Reason" htmlFor="wb-reason" required>
              <Select value={reason} onValueChange={(v) => setReason(v as AdjustmentReason)}>
                <SelectTrigger id="wb-reason" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{reasons.map((r) => <SelectItem key={r} value={r}>{r === "SampleOrGift" ? "Sample / gift / PR" : humanize(r)}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
          </div>
          {(warehouses?.length ?? 0) > 1 && (
            <Field label="From warehouse" htmlFor="wb-wh">
              <Select value={effective} onValueChange={setWarehouseId}>
                <SelectTrigger id="wb-wh" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{warehouses?.map((w) => <SelectItem key={w.id} value={w.id}>{w.code} · {w.name}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
          )}
          <div>
            <p className="eyebrow mb-1.5">Will be deducted</p>
            <ul className="divide-y divide-hairline rounded-md border border-hairline text-sm">
              {bundle.bundleItems.map((b) => {
                const need = b.quantity * qty
                const have = available?.[b.componentProductId]
                return (
                  <li key={b.componentProductId} className="flex justify-between gap-3 px-3 py-2">
                    <span className="min-w-0 truncate">{b.name}</span>
                    <span className={cn("shrink-0 tabular", have !== undefined && need > have ? "text-tone-red" : "text-muted-foreground")}>
                      −{need} <span className="text-xs">({have ?? "…"} in stock)</span>
                    </span>
                  </li>
                )
              })}
            </ul>
          </div>
          <Field label="Notes" htmlFor="wb-notes" required={reason === "Other"} hint="e.g. who it went to.">
            <Textarea id="wb-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} disabled={invalid} variant="destructive">Take out {qty || ""} set{qty === 1 ? "" : "s"}</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
