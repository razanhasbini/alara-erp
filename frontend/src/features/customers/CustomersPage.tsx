import { useCallback, useState } from "react"
import { Link, useSearchParams } from "react-router"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { Field, SearchInput, Segmented, SubmitButton } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { ActivePill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { PhoneInput } from "@/components/common/PhoneInput"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { money, when } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk } from "@/lib/queries"
import type { Customer, Paged } from "@/types/api"
import { formatPhone } from "@/lib/phone"

export function CustomersPage() {
  const { can } = useAuth()
  const [params, setParams] = useSearchParams()
  const [editing, setEditing] = useState<Customer | "new" | null>(null)
  const search = params.get("search") ?? ""
  const status = (params.get("status") ?? "active") as "active" | "inactive" | "all"
  const page = Number(params.get("page") ?? 1)
  const set = useCallback((patch: Record<string, string>) => setParams((previous) => {
    const next = new URLSearchParams(previous)
    for (const [key, value] of Object.entries(patch)) { if (value) next.set(key, value); else next.delete(key) }
    if (!("page" in patch)) next.delete("page")
    return next
  }, { replace: true }), [setParams])
  const query = { search, page, pageSize: 25, isActive: status === "all" ? undefined : status === "active" }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.customers(query),
    queryFn: () => api<Paged<Customer>>("/api/customers", { query }),
    placeholderData: keepPreviousData,
  })

  const columns: Column<Customer>[] = [
    { key: "customer", header: "Customer", cell: (c) => <div className="min-w-0"><div className="truncate font-medium">{c.name}</div><div className="text-xs text-muted-foreground">{formatPhone(c.phone)}{c.city && ` · ${c.city}`}</div></div> },
    { key: "email", header: "Email", hideOnMobile: true, cell: (c) => c.email ?? <span className="text-muted-foreground">—</span> },
    { key: "orders", header: "Orders", align: "right", cell: (c) => <Link to={`/orders?customerId=${c.id}`} onClick={(event) => event.stopPropagation()} className="tabular font-medium text-primary hover:underline">{c.orderCount}</Link> },
    { key: "spent", header: "Total ordered", align: "right", cell: (c) => <span className="tabular font-medium">{money(c.totalSpent)}</span> },
    { key: "paid", header: "Paid", align: "right", hideOnMobile: true, cell: (c) => <span className="tabular">{money(c.totalPaid)}</span> },
    { key: "average", header: "Avg order", align: "right", hideOnMobile: true, cell: (c) => <span className="tabular">{money(c.averageOrderValue)}</span> },
    { key: "last", header: "Last order", hideOnMobile: true, cell: (c) => c.lastOrderAt ? when(c.lastOrderAt) : <span className="text-muted-foreground">Never</span> },
    { key: "status", header: "Status", cell: (c) => <ActivePill active={c.isActive} /> },
  ]

  return (
    <>
      <PageHeader title="Customers" description="Customer profiles and their complete order history. New customers are also created automatically when you save an order."
        actions={can(P.orders.create) && <Button onClick={() => setEditing("new")}><Plus /> New customer</Button>} />
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <SearchInput value={search} onChange={(value) => set({ search: value })} placeholder="Name, phone, email or city" />
        <Segmented label="Customer status" value={status} onChange={(value) => set({ status: value === "active" ? "" : value })}
          options={[{ value: "active", label: "Active" }, { value: "inactive", label: "Inactive" }, { value: "all", label: "All" }]} />
      </div>
      <DataTable columns={columns} rows={data?.items} rowKey={(customer) => customer.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={can(P.orders.update) ? setEditing : undefined}
        empty={<EmptyState title="No customers found">{search ? "Try a different search." : "Customers will appear here when you add one or create an order."}</EmptyState>}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(next) => set({ page: String(next) })} />} />
      {editing && <CustomerDialog customer={editing === "new" ? null : editing} onClose={() => setEditing(null)} />}
    </>
  )
}

function CustomerDialog({ customer, onClose }: { customer: Customer | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(customer?.name ?? "")
  const [phone, setPhone] = useState(customer?.phone ?? "")
  const [email, setEmail] = useState(customer?.email ?? "")
  const [city, setCity] = useState(customer?.city ?? "")
  const [address, setAddress] = useState(customer?.address ?? "")
  const [notes, setNotes] = useState(customer?.notes ?? "")
  const [isActive, setIsActive] = useState(customer?.isActive ?? true)
  const [submitted, setSubmitted] = useState(false)
  const phoneError = !/^\+?[0-9 ()\-/]{6,40}$/.test(phone.trim()) ? "Enter a valid phone number." : undefined
  const emailError = email.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()) ? "Enter a valid email." : undefined
  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), phone: phone.trim(), email: email.trim() || null, city: city.trim() || null, address: address.trim() || null, notes: notes.trim() || null, isActive }
      return customer ? api<Customer>(`/api/customers/${customer.id}`, { method: "PUT", body }) : api<Customer>("/api/customers", { method: "POST", body })
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["sales", "customers"] }); toast.success("Customer saved"); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const submit = () => {
    setSubmitted(true)
    if (name.trim() && !phoneError && !emailError) save.mutate()
  }
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader><DialogTitle>{customer ? `Edit ${customer.name}` : "New customer"}</DialogTitle></DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(event) => { event.preventDefault(); submit() }}>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Name" htmlFor="cu-name" error={submitted && !name.trim() ? "Required." : undefined} required><Input id="cu-name" autoFocus value={name} onChange={(event) => setName(event.target.value)} /></Field>
            <Field label="Phone" htmlFor="cu-phone" error={submitted ? phoneError : undefined} required><PhoneInput id="cu-phone" value={phone} invalid={submitted && !!phoneError} onChange={setPhone} /></Field>
            <Field label="Email" htmlFor="cu-email" error={submitted ? emailError : undefined}><Input id="cu-email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} /></Field>
            <Field label="City / area" htmlFor="cu-city"><Input id="cu-city" value={city} onChange={(event) => setCity(event.target.value)} /></Field>
          </div>
          <Field label="Address" htmlFor="cu-address"><Textarea id="cu-address" rows={2} value={address} onChange={(event) => setAddress(event.target.value)} /></Field>
          <Field label="Customer notes" htmlFor="cu-notes"><Textarea id="cu-notes" rows={2} placeholder="Preferences, sizing, special instructions…" value={notes} onChange={(event) => setNotes(event.target.value)} /></Field>
          <label className="flex items-center gap-2 text-sm"><Switch checked={isActive} onCheckedChange={setIsActive} /> Active customer</label>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending}>Save customer</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
