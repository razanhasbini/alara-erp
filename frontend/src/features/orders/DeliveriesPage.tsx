import { useState } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Pencil, Plus, Trash2 } from "lucide-react"
import { toast } from "sonner"
import { DataTable, type Column } from "@/components/common/DataTable"
import { ConfirmDialog, Field, SubmitButton } from "@/components/common/controls"
import { PageHeader, Section } from "@/components/common/page"
import { ActivePill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { api, errorMessage } from "@/lib/api"
import { dateOnly, money, number, todayIso } from "@/lib/format"
import { qk, useDeliveryCompanies } from "@/lib/queries"
import type { DeliveryCompany, DeliveryExpense, Paged } from "@/types/api"

/** Manager-owned delivery costs. They are intentionally independent from customer orders. */
export function DeliveriesPage() {
  const { data: companies, isLoading: companiesLoading, error: companiesError, refetch: refetchCompanies } = useDeliveryCompanies(true)
  const params = { page: 1, pageSize: 100 }
  const expenses = useQuery({
    queryKey: qk.deliveryExpenses(params),
    queryFn: () => api<Paged<DeliveryExpense>>("/api/delivery-expenses", { query: params }),
  })
  const [editingCompany, setEditingCompany] = useState<DeliveryCompany | "new" | null>(null)
  const [editingExpense, setEditingExpense] = useState<DeliveryExpense | "new" | null>(null)
  const [deletingExpense, setDeletingExpense] = useState<DeliveryExpense | null>(null)
  const totalCost = companies?.reduce((sum, company) => sum + company.deliveryCost, 0) ?? 0
  const deliveryCount = companies?.reduce((sum, company) => sum + company.deliveryCount, 0) ?? 0

  const expenseColumns: Column<DeliveryExpense>[] = [
    { key: "date", header: "Date", cell: (expense) => dateOnly(expense.incurredOn) },
    { key: "company", header: "Delivery company", cell: (expense) => <span className="font-medium">{expense.deliveryCompanyName}</span> },
    { key: "count", header: "Deliveries", align: "right", cell: (expense) => <span className="tabular">{number(expense.deliveryCount)}</span> },
    { key: "amount", header: "Cost", align: "right", cell: (expense) => <span className="tabular font-medium">{money(expense.amount)}</span> },
    { key: "notes", header: "Notes", hideOnMobile: true, cell: (expense) => expense.notes || "—" },
    { key: "by", header: "Recorded by", hideOnMobile: true, cell: (expense) => expense.recordedBy || "—" },
    { key: "actions", header: "", align: "right", cell: (expense) => <div className="flex justify-end gap-1">
      <Button size="icon-sm" variant="ghost" aria-label={`Edit cost for ${expense.deliveryCompanyName}`} onClick={() => setEditingExpense(expense)}><Pencil /></Button>
      <Button size="icon-sm" variant="ghost" aria-label={`Delete cost for ${expense.deliveryCompanyName}`} onClick={() => setDeletingExpense(expense)}><Trash2 /></Button>
    </div> },
  ]
  const companyColumns: Column<DeliveryCompany>[] = [
    { key: "name", header: "Delivery company", cell: (company) => <div><div className="font-medium">{company.name}</div>{company.phone && <div className="text-xs text-muted-foreground">{company.phone}</div>}</div> },
    { key: "deliveries", header: "Deliveries", align: "right", cell: (company) => <span className="tabular">{company.deliveryCount ? number(company.deliveryCount) : "—"}</span> },
    { key: "cost", header: "Total cost", align: "right", cell: (company) => <span className="tabular font-medium">{company.deliveryCost ? money(company.deliveryCost) : "—"}</span> },
    { key: "status", header: "Status", hideOnMobile: true, cell: (company) => <ActivePill active={company.isActive} /> },
    { key: "actions", header: "", align: "right", cell: (company) => <Button size="xs" variant="ghost" onClick={() => setEditingCompany(company)}>Edit</Button> },
  ]

  return (
    <>
      <PageHeader title="Deliveries"
        description="Record delivery costs here. They are separate from orders and are included in profit analytics."
        meta={<><span className="text-sm">Recorded deliveries <strong className="tabular">{number(deliveryCount)}</strong></span><span className="text-sm">Total cost <strong className="tabular">{money(totalCost)}</strong></span></>}
        actions={<><Button variant="outline" onClick={() => setEditingCompany("new")}><Plus /> Add company</Button><Button onClick={() => setEditingExpense("new")} disabled={!companies?.some((company) => company.isActive)}><Plus /> Record delivery cost</Button></>} />

      <div className="grid gap-6">
        <Section title="Delivery costs">
          <DataTable columns={expenseColumns} rows={expenses.data?.items} rowKey={(expense) => expense.id}
            loading={expenses.isLoading} error={expenses.error} onRetry={expenses.refetch}
            className="rounded-none border-0"
            empty={<EmptyState title="No delivery costs recorded">Record a daily, weekly, or monthly delivery invoice when you receive it.</EmptyState>} />
        </Section>
        <Section title="Delivery companies">
          <DataTable columns={companyColumns} rows={companies} rowKey={(company) => company.id}
            loading={companiesLoading} error={companiesError} onRetry={refetchCompanies}
            className="rounded-none border-0"
            empty={<EmptyState title="No delivery companies yet">Add the delivery companies you work with.</EmptyState>} />
        </Section>
      </div>

      {editingExpense && <ExpenseDialog expense={editingExpense === "new" ? null : editingExpense} companies={companies ?? []} onClose={() => setEditingExpense(null)} />}
      {editingCompany && <CompanyDialog company={editingCompany === "new" ? null : editingCompany} onClose={() => setEditingCompany(null)} />}
      {deletingExpense && <DeleteExpenseDialog expense={deletingExpense} onClose={() => setDeletingExpense(null)} />}
    </>
  )
}

function ExpenseDialog({ expense, companies, onClose }: { expense: DeliveryExpense | null; companies: DeliveryCompany[]; onClose: () => void }) {
  const queryClient = useQueryClient()
  const selectable = companies.filter((company) => company.isActive || company.id === expense?.deliveryCompanyId)
  const [companyId, setCompanyId] = useState(expense?.deliveryCompanyId ?? selectable[0]?.id ?? "")
  const [incurredOn, setIncurredOn] = useState(expense?.incurredOn ?? todayIso())
  const [amount, setAmount] = useState(expense?.amount.toFixed(2) ?? "")
  const [deliveryCount, setDeliveryCount] = useState(String(expense?.deliveryCount ?? 1))
  const [notes, setNotes] = useState(expense?.notes ?? "")
  const amountValid = /^\d{1,9}(\.\d{1,2})?$/.test(amount) && Number(amount) > 0
  const countValid = /^\d+$/.test(deliveryCount) && Number(deliveryCount) > 0
  const save = useMutation({
    mutationFn: () => api(expense ? `/api/delivery-expenses/${expense.id}` : "/api/delivery-expenses", {
      method: expense ? "PUT" : "POST",
      body: { deliveryCompanyId: companyId, incurredOn, amount: Number(amount), deliveryCount: Number(deliveryCount), notes: notes.trim() || null },
    }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["sales"] }); toast.success(expense ? "Delivery cost updated" : "Delivery cost recorded"); onClose() },
    onError: (error) => toast.error(errorMessage(error)),
  })
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{expense ? "Edit delivery cost" : "Record delivery cost"}</DialogTitle></DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(event) => { event.preventDefault(); if (companyId && amountValid && countValid) save.mutate() }}>
          <Field label="Delivery company" htmlFor="de-company" required>
            <Select value={companyId} onValueChange={setCompanyId}><SelectTrigger id="de-company" className="w-full"><SelectValue placeholder="Choose a company" /></SelectTrigger><SelectContent>{selectable.map((company) => <SelectItem key={company.id} value={company.id}>{company.name}</SelectItem>)}</SelectContent></Select>
          </Field>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="Date" htmlFor="de-date" required><Input id="de-date" type="date" max={todayIso()} value={incurredOn} onChange={(event) => setIncurredOn(event.target.value)} /></Field>
            <Field label="Cost (USD)" htmlFor="de-amount" required error={amount && !amountValid ? "Enter an amount greater than zero, with up to 2 decimal places." : undefined}><Input id="de-amount" inputMode="decimal" value={amount} onChange={(event) => setAmount(event.target.value)} /></Field>
            <Field label="Deliveries covered" htmlFor="de-count" required error={deliveryCount && !countValid ? "Enter a whole number greater than zero." : undefined}><Input id="de-count" inputMode="numeric" value={deliveryCount} onChange={(event) => setDeliveryCount(event.target.value)} /></Field>
          </div>
          <Field label="Notes" htmlFor="de-notes" hint="For example: October invoice or delivery dates covered."><Textarea id="de-notes" rows={3} value={notes} onChange={(event) => setNotes(event.target.value)} /></Field>
          <DialogFooter><Button type="button" variant="ghost" onClick={onClose}>Cancel</Button><SubmitButton pending={save.isPending} disabled={!companyId || !incurredOn || !amountValid || !countValid}>Save cost</SubmitButton></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function DeleteExpenseDialog({ expense, onClose }: { expense: DeliveryExpense; onClose: () => void }) {
  const queryClient = useQueryClient()
  const remove = useMutation({
    mutationFn: () => api(`/api/delivery-expenses/${expense.id}`, { method: "DELETE" }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["sales"] }); toast.success("Delivery cost deleted"); onClose() },
    onError: (error) => toast.error(errorMessage(error)),
  })
  return <ConfirmDialog open onOpenChange={(open) => !open && onClose()} title="Delete this delivery cost?"
    description={`${expense.deliveryCompanyName} · ${dateOnly(expense.incurredOn)} · ${money(expense.amount)}`}
    confirmLabel="Delete cost" destructive pending={remove.isPending} onConfirm={() => remove.mutate()} />
}

function CompanyDialog({ company, onClose }: { company: DeliveryCompany | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState(company?.name ?? "")
  const [phone, setPhone] = useState(company?.phone ?? "")
  const [isActive, setIsActive] = useState(company?.isActive ?? true)
  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), phone: phone.trim() || null, isActive }
      return company ? api(`/api/delivery-companies/${company.id}`, { method: "PUT", body }) : api("/api/delivery-companies", { method: "POST", body })
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["sales"] }); toast.success("Delivery company saved"); onClose() },
    onError: (error) => toast.error(errorMessage(error)),
  })
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{company ? `Edit ${company.name}` : "Add delivery company"}</DialogTitle></DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(event) => { event.preventDefault(); if (name.trim()) save.mutate() }}>
          <Field label="Name" htmlFor="dc-name" required><Input id="dc-name" value={name} onChange={(event) => setName(event.target.value)} /></Field>
          <Field label="Phone" htmlFor="dc-phone"><Input id="dc-phone" type="tel" value={phone} onChange={(event) => setPhone(event.target.value)} /></Field>
          <label className="flex items-center gap-2 text-sm"><Switch checked={isActive} onCheckedChange={setIsActive} /> Active</label>
          <DialogFooter><Button type="button" variant="ghost" onClick={onClose}>Cancel</Button><SubmitButton pending={save.isPending} disabled={!name.trim()}>Save</SubmitButton></DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
