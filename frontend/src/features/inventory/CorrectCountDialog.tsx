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
import { useWarehouses } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { ProductStock } from "@/types/api"
import type { CountTarget } from "./countTarget"

/** Fixes a miscount: staff type the number actually on the shelf; the difference is written to the movement ledger. */
export function CorrectCountDialog({ target, onClose }: { target: CountTarget; onClose: () => void }) {
  const queryClient = useQueryClient()
  const { data: active } = useWarehouses()
  // A product never stocked has no warehouse rows yet: offer every active warehouse, starting from 0.
  const warehouses = target.warehouses.length > 0 ? target.warehouses
    : (active ?? []).map((w) => ({ warehouseId: w.id, code: w.code, onHand: 0 }))
  const [picked, setPicked] = useState<string | null>(null)
  const warehouseId = picked ?? warehouses[0]?.warehouseId ?? ""
  const recorded = warehouses.find((w) => w.warehouseId === warehouseId)?.onHand ?? 0
  const [counted, setCounted] = useState(String(recorded))
  const [notes, setNotes] = useState("")
  const [expiry, setExpiry] = useState("")
  const qty = Number(counted)
  const countError = !/^\d+$/.test(counted) ? "Enter a whole number, 0 or more." : undefined
  const change = countError ? 0 : qty - recorded
  const expiryError = target.needsExpiry && change > 0 && !expiry ? "This product tracks expiry dates." : undefined

  const save = useMutation({
    mutationFn: () => api<ProductStock>("/api/inventory/counts", {
      method: "POST",
      body: {
        warehouseId, productId: target.productId, countedQuantity: qty, batchId: target.batch?.id ?? null,
        notes: notes.trim() || null, expiryDate: target.needsExpiry && expiry ? expiry : null,
      },
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["inventory"] })
      await queryClient.invalidateQueries({ queryKey: ["dashboard"] })
      toast.success(`${target.productName}: count set to ${qty}`)
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Correct count · {target.productName}</DialogTitle>
          <DialogDescription>
            {target.batch ? `How many units are left from ${target.batch.label}?` : "How many units are actually on the shelf?"}{" "}
            The difference is saved in the movement history as a count correction.
          </DialogDescription>
        </DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (warehouseId && !countError && !expiryError && change !== 0) save.mutate() }}>
          {warehouses.length > 1 && (
            <Field label="Warehouse" htmlFor="count-warehouse">
              <Select value={warehouseId} onValueChange={(v) => { setPicked(v); setCounted(String(warehouses.find((w) => w.warehouseId === v)?.onHand ?? 0)) }}>
                <SelectTrigger id="count-warehouse" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{warehouses.map((w) => <SelectItem key={w.warehouseId} value={w.warehouseId}>{w.code}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
          )}
          <div className="grid grid-cols-2 items-start gap-4">
            <div>
              <p className="mb-2 text-sm font-medium">Recorded now</p>
              <div className="flex h-9 items-center font-display text-2xl tabular text-muted-foreground">{recorded}</div>
            </div>
            <Field label="Actual count" htmlFor="count-qty" error={countError} required>
              <Input id="count-qty" inputMode="numeric" autoFocus value={counted} onChange={(e) => setCounted(e.target.value)}
                onFocus={(e) => e.target.select()} className="tabular" />
            </Field>
          </div>
          <p className={cn("text-sm", change > 0 ? "text-tone-green" : change < 0 ? "text-tone-red" : "text-muted-foreground")}>
            {change > 0 ? `${change} unit(s) will be added.` : change < 0 ? `${-change} unit(s) will be removed.` : "No change yet."}
          </p>
          {target.needsExpiry && change > 0 && (
            <Field label="Expiry date" htmlFor="count-expiry" error={expiryError} required>
              <Input id="count-expiry" type="date" value={expiry} onChange={(e) => setExpiry(e.target.value)} />
            </Field>
          )}
          <Field label="Notes" htmlFor="count-notes" hint="Optional, e.g. “typed 50 instead of 30 when receiving”.">
            <Textarea id="count-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} disabled={save.isPending || change === 0 || !!countError}>Save count</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
