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
import { useSettings } from "@/lib/queries"
import type { AdjustmentReason } from "@/types/api"

export interface AdjustTarget {
  productId: string
  productName: string
  tracksExpiry: boolean
  warehouseId: string
  direction: "increase" | "decrease"
  batch?: { id: string; number: string; onHand: number; isExpired: boolean }
}

const reasons: AdjustmentReason[] = ["CountCorrection", "Damaged", "Expired", "Lost", "SampleOrGift", "Other"]

/** Manual stock correction. Every adjustment needs a reason and is written to the movement ledger. */
export function AdjustStockDialog({ target, onClose }: { target: AdjustTarget; onClose: () => void }) {
  const queryClient = useQueryClient()
  const { data: settings } = useSettings()
  const expiryEnabled = settings?.expiryDatesEnabled ?? false
  const decrease = target.direction === "decrease"
  const [quantity, setQuantity] = useState("1")
  const [reason, setReason] = useState<AdjustmentReason>(target.batch?.isExpired ? "Expired" : "CountCorrection")
  const [notes, setNotes] = useState("")
  const [batchNumber, setBatchNumber] = useState("")
  const [expiry, setExpiry] = useState("")
  const qty = Number(quantity)
  const qtyError = !/^\d+$/.test(quantity) || qty < 1 ? "Enter a whole number above zero."
    : decrease && target.batch && qty > target.batch.onHand ? `This batch has ${target.batch.onHand}.` : undefined
  const notesError = reason === "Other" && !notes.trim() ? "Explain the adjustment." : undefined
  const expiryError = expiryEnabled && !decrease && target.tracksExpiry && !expiry ? "This product tracks expiry dates." : undefined
  const availableReasons = expiryEnabled ? reasons : reasons.filter((value) => value !== "Expired")

  const save = useMutation({
    mutationFn: () => api("/api/inventory/adjustments", {
      method: "POST",
      body: {
        warehouseId: target.warehouseId, productId: target.productId, quantityChange: decrease ? -qty : qty, reason,
        notes: notes.trim() || null, batchId: target.batch?.id ?? null,
        batchNumber: !decrease && batchNumber.trim() ? batchNumber.trim() : null, expiryDate: expiryEnabled && !decrease && expiry ? expiry : null,
      },
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["inventory"] })
      await queryClient.invalidateQueries({ queryKey: ["dashboard"] })
      toast.success(decrease ? `Removed ${qty} unit(s)` : `Added ${qty} unit(s)`)
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{decrease ? "Remove stock" : "Add stock"} · {target.productName}</DialogTitle>
          <DialogDescription>
            {decrease ? `From batch ${target.batch?.number}. Reserved units cannot be removed.` : "For corrections found during a count. Use “Receive stock” for supplier deliveries."}
          </DialogDescription>
        </DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (!qtyError && !notesError && !expiryError) save.mutate() }}>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Units" htmlFor="adj-qty" error={qtyError} required>
              <Input id="adj-qty" inputMode="numeric" value={quantity} onChange={(e) => setQuantity(e.target.value)} className="tabular" />
            </Field>
            <Field label="Reason" htmlFor="adj-reason" required>
              <Select value={reason} onValueChange={(v) => setReason(v as AdjustmentReason)}>
                <SelectTrigger id="adj-reason" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{availableReasons.map((r) => <SelectItem key={r} value={r}>{r === "SampleOrGift" ? "Sample / gift" : humanize(r)}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
          </div>
          {!decrease && (
            <div className={expiryEnabled ? "grid grid-cols-2 gap-4" : "grid gap-4"}>
              <Field label="Batch number" htmlFor="adj-batch" hint="Existing or new; blank = today's adjustment batch.">
                <Input id="adj-batch" className="font-mono" value={batchNumber} onChange={(e) => setBatchNumber(e.target.value)} />
              </Field>
              {expiryEnabled && <Field label="Expiry date" htmlFor="adj-expiry" error={expiryError} required={target.tracksExpiry}>
                <Input id="adj-expiry" type="date" value={expiry} onChange={(e) => setExpiry(e.target.value)} />
              </Field>}
            </div>
          )}
          <Field label="Notes" htmlFor="adj-notes" error={notesError} required={reason === "Other"}>
            <Textarea id="adj-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} variant={decrease ? "destructive" : "default"}>{decrease ? "Remove units" : "Add units"}</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
