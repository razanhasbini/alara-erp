import { useState } from "react"
import { Link, useSearchParams } from "react-router"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Minus, Pencil, Plus, SlidersHorizontal, X } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, type Column } from "@/components/common/DataTable"
import { Field, SearchInput, Segmented, SubmitButton } from "@/components/common/controls"
import { Facts, PageHeader } from "@/components/common/page"
import { ProductPicker } from "@/components/common/ProductPicker"
import { ActivePill, StockStatusPill } from "@/components/common/StatusBadge"
import { EmptyState, ErrorState, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { dateTime, money, number } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Box, BoxCountAction, BoxMovement, BoxMovementType, StockStatus } from "@/types/api"

const DEFAULT_ALERT_LEVEL = 500

type Filter = "All" | "Low" | "Out"

const movementLabel: Record<BoxMovementType, string> = { Added: "Added", Removed: "Taken out", Counted: "Count corrected" }

const contentsText = (box: Box) => box.contents.map((c) => `${c.quantity} × ${c.productName}`).join(", ")

export function BoxesPage() {
  const { can } = useAuth()
  const [params, setParams] = useSearchParams()
  const boxId = params.get("box")
  const [search, setSearch] = useState("")
  const [filter, setFilter] = useState<Filter>("All")
  const [showInactive, setShowInactive] = useState(false)
  const [creating, setCreating] = useState(false)
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.boxes(showInactive), queryFn: () => api<Box[]>("/api/boxes", { query: { includeInactive: showInactive } }),
  })
  const term = search.trim().toLowerCase()
  const rows = data?.filter((b) =>
    (filter === "All" || (filter === "Low" ? b.status !== "InStock" : b.status === "Out"))
    && (!term || b.name.toLowerCase().includes(term) || b.contents.some((c) => c.productName.toLowerCase().includes(term))))
  const open = (id: string | null) => setParams((prev) => {
    const next = new URLSearchParams(prev)
    if (id) next.set("box", id); else next.delete("box")
    return next
  }, { replace: true })

  const columns: Column<Box>[] = [
    { key: "box", header: "Box", cell: (b) => (
      <div className="min-w-0">
        <div className={cn("font-medium", !b.isActive && "text-muted-foreground")}>{b.name}</div>
        <div className="line-clamp-1 text-xs text-muted-foreground">{b.contents.length > 0 ? `For ${contentsText(b)}` : b.notes ?? "No products linked"}</div>
      </div>
    ) },
    { key: "stock", header: "In stock", align: "right", cell: (b) => <span className={cn("tabular font-semibold", b.status === "Out" && "text-tone-red", b.status === "Low" && "text-tone-amber")}>{number(b.onHand)}</span> },
    { key: "alert", header: "Alert at", align: "right", hideOnMobile: true, cell: (b) => <span className="tabular text-muted-foreground">{number(b.lowStockThreshold)}</span> },
    { key: "price", header: "Sells for", align: "right", hideOnMobile: true, cell: (b) => <span className="tabular text-muted-foreground">{b.price !== null ? money(b.price) : "—"}</span> },
    { key: "status", header: "Status", cell: (b) => b.isActive ? <StockStatusPill status={b.status} /> : <ActivePill active={false} /> },
  ]

  return (
    <>
      <PageHeader title="Boxes" description="Packaging boxes and how many are left. Selling an order doesn't change these counts — add, take out or correct them here."
        actions={can(P.boxes.manage) && <Button onClick={() => setCreating(true)}><Plus /> New box</Button>} />
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput value={search} onChange={setSearch} placeholder="Search box or product" />
        <Segmented label="Box filter" value={filter} onChange={(v) => setFilter(v as Filter)}
          options={[{ value: "All", label: "All" }, { value: "Low", label: "Low" }, { value: "Out", label: "Out" }]} />
        <label className="ml-auto flex items-center gap-2 text-sm text-muted-foreground">
          <Switch checked={showInactive} onCheckedChange={setShowInactive} /> Show inactive
        </label>
      </div>
      <DataTable columns={columns} rows={rows} rowKey={(b) => b.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={(b) => open(b.id)}
        empty={<EmptyState title={term || filter !== "All" ? "No boxes match" : "No boxes yet"}>
          {!term && filter === "All" ? "Add each kind of box you pack orders in, with the products it's made for." : null}</EmptyState>} />
      {boxId && <BoxSheet boxId={boxId} onClose={() => open(null)} />}
      {creating && <BoxFormDialog box={null} onClose={() => setCreating(false)} onSaved={(b) => open(b.id)} />}
    </>
  )
}

function BoxSheet({ boxId, onClose }: { boxId: string; onClose: () => void }) {
  const { can } = useAuth()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [action, setAction] = useState<BoxCountAction | null>(null)
  const { data: box, isLoading, error } = useQuery({ queryKey: qk.box(boxId), queryFn: () => api<Box>(`/api/boxes/${boxId}`) })
  const { data: history } = useQuery({ queryKey: qk.boxHistory(boxId), queryFn: () => api<BoxMovement[]>(`/api/boxes/${boxId}/history`) })
  const manage = can(P.boxes.manage)
  const setActive = useMutation({
    mutationFn: (isActive: boolean) => api<Box>(`/api/boxes/${boxId}/active`, { method: "PUT", body: { isActive } }),
    onSuccess: async (b) => { await queryClient.invalidateQueries({ queryKey: ["boxes"] }); toast.success(b.isActive ? `${b.name} reactivated` : `${b.name} deactivated`) },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        {isLoading || !box ? (
          error ? <div className="p-4"><ErrorState error={error} /></div> : <div className="grid h-40 place-items-center"><InlineSpinner /></div>
        ) : (
          <>
            <SheetHeader>
              <SheetTitle className="font-display text-xl">{box.name}</SheetTitle>
              <SheetDescription>{box.isActive ? "Packaging box" : "Inactive box"}{box.notes && <> · {box.notes}</>}</SheetDescription>
            </SheetHeader>
            <div className="grid gap-6 px-4 pb-6">
              <div className="border-y border-hairline py-3 text-center">
                <div className={cn("font-display text-3xl tabular", box.status === "Out" && "text-tone-red", box.status === "Low" && "text-tone-amber")}>{number(box.onHand)}</div>
                <div className="eyebrow">In stock · alert at {number(box.lowStockThreshold)}</div>
                {manage && box.isActive && (
                  <div className="mt-3 flex flex-wrap justify-center gap-2">
                    <Button size="xs" variant="outline" onClick={() => setAction("Add")}><Plus /> Add boxes</Button>
                    <Button size="xs" variant="outline" onClick={() => setAction("Remove")}><Minus /> Take out</Button>
                    <Button size="xs" variant="outline" onClick={() => setAction("Count")}><SlidersHorizontal /> Correct count</Button>
                  </div>
                )}
              </div>

              <div>
                <div className="mb-2 flex items-center justify-between">
                  <p className="eyebrow">Made for</p>
                  {manage && <Button size="xs" variant="ghost" onClick={() => setEditing(true)}><Pencil /> Edit box</Button>}
                </div>
                {box.contents.length === 0 ? <p className="text-sm text-muted-foreground">No products linked.</p> : (
                  <ul className="divide-y divide-hairline rounded-md border border-hairline text-sm">
                    {box.contents.map((c) => (
                      <li key={c.productId} className="flex justify-between gap-3 px-3 py-2">
                        <Link to={`/products/${c.productId}`} className="hover:underline">{c.productName}{c.productType === "Bundle" && <span className="text-muted-foreground"> · bundle</span>}</Link>
                        <span className="tabular text-muted-foreground">× {c.quantity}</span>
                      </li>
                    ))}
                  </ul>
                )}
                {box.price !== null && <Facts className="mt-3" items={[["Sells for", <span key="price" className="tabular">{money(box.price)}</span>]]} />}
              </div>

              <div>
                <p className="eyebrow mb-2">History</p>
                {!history ? <InlineSpinner /> : history.length === 0 ? <p className="text-sm text-muted-foreground">No changes yet.</p> : (
                  <ul className="divide-y divide-hairline rounded-md border border-hairline text-sm">
                    {history.map((m) => (
                      <li key={m.id} className="px-3 py-2">
                        <div className="flex items-baseline justify-between gap-3">
                          <span>{movementLabel[m.type]} <span className={cn("tabular font-medium", m.quantityChange > 0 ? "text-tone-green" : "text-tone-red")}>{m.quantityChange > 0 ? "+" : "−"}{number(Math.abs(m.quantityChange))}</span></span>
                          <span className="tabular text-xs text-muted-foreground">now {number(m.quantityAfter)}</span>
                        </div>
                        <div className="text-xs text-muted-foreground">{dateTime(m.createdAt)}{m.performedBy && <> · {m.performedBy}</>}{m.notes && <> · {m.notes}</>}</div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>

              {manage && (
                <Button variant="ghost" size="sm" className="justify-self-start px-0 text-muted-foreground" disabled={setActive.isPending}
                  onClick={() => setActive.mutate(!box.isActive)}>
                  {box.isActive ? "Deactivate this box" : "Reactivate this box"}
                </Button>
              )}
            </div>
            {editing && <BoxFormDialog box={box} onClose={() => setEditing(false)} />}
            {action && <BoxCountDialog box={box} action={action} onClose={() => setAction(null)} />}
          </>
        )}
      </SheetContent>
    </Sheet>
  )
}

interface ContentLine { productId: string; name: string; quantity: string }

function BoxFormDialog({ box, onClose, onSaved }: { box: Box | null; onClose: () => void; onSaved?: (box: Box) => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(box?.name ?? "")
  const [contents, setContents] = useState<ContentLine[]>(box?.contents.map((c) => ({ productId: c.productId, name: c.productName, quantity: String(c.quantity) })) ?? [])
  const [threshold, setThreshold] = useState(String(box?.lowStockThreshold ?? DEFAULT_ALERT_LEVEL))
  const [price, setPrice] = useState(box?.price != null ? box.price.toFixed(2) : "")
  const [notes, setNotes] = useState(box?.notes ?? "")
  const [initialCount, setInitialCount] = useState("")
  const [submitted, setSubmitted] = useState(false)
  const errors = {
    name: !name.trim() ? "Required." : undefined,
    threshold: !/^\d{1,8}$/.test(threshold) ? "Whole number." : undefined,
    price: price && !/^\d{0,7}(\.\d{0,2})?$/.test(price) ? "Invalid amount." : undefined,
    initialCount: initialCount && !/^\d{1,8}$/.test(initialCount) ? "Whole number." : undefined,
    contents: contents.some((c) => !/^\d{1,4}$/.test(c.quantity) || Number(c.quantity) < 1),
  }
  const invalid = Object.values(errors).some(Boolean)
  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: name.trim(), notes: notes.trim() || null, price: price ? Number(price) : null, lowStockThreshold: Number(threshold),
        contents: contents.map((c) => ({ productId: c.productId, quantity: Number(c.quantity) })),
        initialCount: !box && initialCount ? Number(initialCount) : null,
      }
      return box ? api<Box>(`/api/boxes/${box.id}`, { method: "PUT", body }) : api<Box>("/api/boxes", { method: "POST", body })
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ["boxes"] })
      toast.success(box ? `${saved.name} saved` : `${saved.name} added`)
      onClose()
      onSaved?.(saved)
    },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const show = (key: keyof typeof errors) => submitted ? errors[key] : undefined

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{box ? `Edit ${box.name}` : "New box"}</DialogTitle>
          <DialogDescription>{box ? "To change how many are left, use Add boxes, Take out or Correct count." : "A kind of box you pack orders in, and the products it's made for."}</DialogDescription>
        </DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); setSubmitted(true); if (!invalid) save.mutate() }}>
          <Field label="Name" htmlFor="box-name" error={show("name") as string | undefined} required>
            <Input id="box-name" autoFocus={!box} placeholder="e.g. Curls On shampoo box" value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <div className="grid gap-2">
            <p className="text-sm font-medium">What goes in it</p>
            {contents.length > 0 && (
              <ul className="divide-y divide-hairline rounded-md border border-hairline">
                {contents.map((c) => (
                  <li key={c.productId} className="flex items-center gap-2 px-3 py-1.5 text-sm">
                    <span className="mr-auto min-w-0 truncate">{c.name}</span>
                    <Input aria-label={`How many ${c.name}`} inputMode="numeric" value={c.quantity}
                      onChange={(e) => setContents((all) => all.map((x) => x.productId === c.productId ? { ...x, quantity: e.target.value } : x))}
                      className={cn("h-7 w-16 text-center tabular", submitted && (!/^\d{1,4}$/.test(c.quantity) || Number(c.quantity) < 1) && "border-destructive")} />
                    <Button type="button" size="icon-xs" variant="ghost" aria-label={`Remove ${c.name}`} onClick={() => setContents((all) => all.filter((x) => x.productId !== c.productId))}><X /></Button>
                  </li>
                ))}
              </ul>
            )}
            <ProductPicker exclude={contents.map((c) => c.productId)} placeholder="Add a product or bundle…"
              onSelect={(p) => setContents((all) => [...all, { productId: p.id, name: p.name, quantity: "1" }])} />
            <p className="text-xs text-muted-foreground">Optional. Used to show how many boxes are left when these products are sold or received.</p>
          </div>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Alert when at or below" htmlFor="box-threshold" error={show("threshold") as string | undefined} required>
              <Input id="box-threshold" inputMode="numeric" value={threshold} onChange={(e) => setThreshold(e.target.value)} className="tabular" />
            </Field>
            <Field label="Sells for ($)" htmlFor="box-price" error={show("price") as string | undefined} hint="Only if customers can buy it, e.g. the PR box.">
              <Input id="box-price" inputMode="decimal" placeholder="—" value={price} onChange={(e) => setPrice(e.target.value)} className="tabular" />
            </Field>
          </div>
          {!box && (
            <Field label="Boxes you have now" htmlFor="box-count" error={show("initialCount") as string | undefined} hint="Leave empty if you'll count them later.">
              <Input id="box-count" inputMode="numeric" value={initialCount} onChange={(e) => setInitialCount(e.target.value)} className="w-32 tabular" />
            </Field>
          )}
          <Field label="Notes" htmlFor="box-notes">
            <Textarea id="box-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending}>{box ? "Save box" : "Add box"}</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

const actionText: Record<BoxCountAction, { title: string; description: string; label: string; button: string }> = {
  Add: { title: "Add boxes", description: "Boxes delivered or found.", label: "Boxes to add", button: "Add boxes" },
  Remove: { title: "Take out boxes", description: "Boxes used, damaged or given away.", label: "Boxes to take out", button: "Take out" },
  Count: { title: "Correct count", description: "Type how many boxes are actually there.", label: "Actual count", button: "Save count" },
}

function BoxCountDialog({ box, action, onClose }: { box: Box; action: BoxCountAction; onClose: () => void }) {
  const queryClient = useQueryClient()
  const text = actionText[action]
  const [quantity, setQuantity] = useState(action === "Count" ? String(box.onHand) : "")
  const [notes, setNotes] = useState("")
  const qty = Number(quantity)
  const after = action === "Add" ? box.onHand + qty : action === "Remove" ? box.onHand - qty : qty
  const error = !/^\d{1,8}$/.test(quantity) ? (action === "Count" ? "Enter a whole number, 0 or more." : "Enter how many boxes.")
    : action !== "Count" && qty < 1 ? "Enter how many boxes."
    : action === "Remove" && qty > box.onHand ? `Only ${number(box.onHand)} left.`
    : action === "Count" && qty === box.onHand ? "That's the count already shown." : undefined
  const save = useMutation({
    mutationFn: () => api<Box>(`/api/boxes/${box.id}/count`, { method: "POST", body: { action, quantity: qty, notes: notes.trim() || null } }),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ["boxes"] })
      toast.success(`${saved.name}: ${number(saved.onHand)} left`)
      onClose()
    },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const afterStatus: StockStatus = after <= 0 ? "Out" : after <= box.lowStockThreshold ? "Low" : "InStock"

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{text.title} · {box.name}</DialogTitle>
          <DialogDescription>{text.description} Saved in the box history.</DialogDescription>
        </DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (!error) save.mutate() }}>
          <div className="grid grid-cols-2 items-start gap-4">
            <div>
              <p className="mb-2 text-sm font-medium">Now</p>
              <div className="flex h-9 items-center font-display text-2xl tabular text-muted-foreground">{number(box.onHand)}</div>
            </div>
            <Field label={text.label} htmlFor="boxcount-qty" error={quantity ? error : undefined} required>
              <Input id="boxcount-qty" inputMode="numeric" autoFocus value={quantity} onChange={(e) => setQuantity(e.target.value)}
                onFocus={(e) => e.target.select()} className="tabular" />
            </Field>
          </div>
          {!error && (
            <p className={cn("text-sm", afterStatus === "InStock" ? "text-muted-foreground" : "text-tone-amber")}>
              {number(after)} left after this{afterStatus !== "InStock" && ` — at or below the alert level of ${number(box.lowStockThreshold)}`}.
            </p>
          )}
          <Field label="Notes" htmlFor="boxcount-notes" hint={action === "Remove" ? "Optional, e.g. “damaged in storage”." : "Optional."}>
            <Textarea id="boxcount-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending} disabled={save.isPending || !!error}>{text.button}</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
