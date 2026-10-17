import { useState } from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { Plus } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, type Column } from "@/components/common/DataTable"
import { Field, SubmitButton } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { ActivePill } from "@/components/common/StatusBadge"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { number } from "@/lib/format"
import { P } from "@/lib/permissions"
import { useWarehouses } from "@/lib/queries"
import type { Warehouse } from "@/types/api"

export function WarehousesPage() {
  const { can } = useAuth()
  const { data, isLoading, error, refetch } = useWarehouses(true)
  const [editing, setEditing] = useState<Warehouse | "new" | null>(null)
  const columns: Column<Warehouse>[] = [
    { key: "code", header: "Code", cell: (w) => <span className="code font-medium">{w.code}</span> },
    { key: "name", header: "Name", cell: (w) => <div>{w.name}{w.address && <div className="text-xs text-muted-foreground">{w.address}</div>}</div> },
    { key: "products", header: "Products in stock", align: "right", hideOnMobile: true, cell: (w) => number(w.productsInStock) },
    { key: "units", header: "Units on hand", align: "right", cell: (w) => <span className="tabular font-medium">{number(w.unitsOnHand)}</span> },
    { key: "status", header: "Status", cell: (w) => <ActivePill active={w.isActive} /> },
  ]
  return (
    <>
      <PageHeader title="Warehouses" description="Places where stock is kept. Orders ship from one warehouse."
        actions={can(P.warehouses.manage) && <Button onClick={() => setEditing("new")}><Plus /> New warehouse</Button>} />
      <DataTable columns={columns} rows={data} rowKey={(w) => w.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={can(P.warehouses.manage) ? (w) => setEditing(w) : undefined} />
      {editing && <WarehouseDialog warehouse={editing === "new" ? null : editing} onClose={() => setEditing(null)} />}
    </>
  )
}

function WarehouseDialog({ warehouse, onClose }: { warehouse: Warehouse | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(warehouse?.name ?? "")
  const [code, setCode] = useState(warehouse?.code ?? "")
  const [address, setAddress] = useState(warehouse?.address ?? "")
  const [isActive, setIsActive] = useState(warehouse?.isActive ?? true)
  const codeError = !/^[A-Za-z0-9][A-Za-z0-9_-]{0,29}$/.test(code) ? "Letters, digits, - or _ (max 30)." : undefined
  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), code: code.trim(), address: address.trim() || null, isActive }
      return warehouse ? api(`/api/warehouses/${warehouse.id}`, { method: "PUT", body }) : api("/api/warehouses", { method: "POST", body })
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["inventory"] }); toast.success("Warehouse saved"); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{warehouse ? `Edit ${warehouse.code}` : "New warehouse"}</DialogTitle></DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (name.trim() && !codeError) save.mutate() }}>
          <div className="grid grid-cols-[8rem_1fr] gap-4">
            <Field label="Code" htmlFor="wh-code" error={code ? codeError : undefined} required><Input id="wh-code" className="font-mono uppercase" value={code} onChange={(e) => setCode(e.target.value)} /></Field>
            <Field label="Name" htmlFor="wh-name" required><Input id="wh-name" value={name} onChange={(e) => setName(e.target.value)} /></Field>
          </div>
          <Field label="Address" htmlFor="wh-addr"><Textarea id="wh-addr" rows={2} value={address} onChange={(e) => setAddress(e.target.value)} /></Field>
          <label className="flex items-center gap-2 text-sm"><Switch checked={isActive} onCheckedChange={setIsActive} /> Active</label>
          {warehouse && warehouse.unitsOnHand > 0 && !isActive && (
            <p className="text-xs text-tone-amber">This warehouse holds stock; it must be moved out before it can be deactivated.</p>
          )}
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} disabled={!name.trim() || !!codeError}>Save</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
