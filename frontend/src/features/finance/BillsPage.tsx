import { useState } from "react"
import { Link, useSearchParams } from "react-router"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ChevronLeft, ChevronRight, Pencil, Plus, Trash2 } from "lucide-react"
import { toast } from "sonner"
import { ConfirmDialog, Field, SubmitButton } from "@/components/common/controls"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { FigureRow } from "@/components/common/figures"
import { PageHeader } from "@/components/common/page"
import { Pill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, ApiError, errorMessage } from "@/lib/api"
import { dateOnly, money, todayIso } from "@/lib/format"
import { qk } from "@/lib/queries"
import type { Expense, ExpenseCategory, ExpenseList } from "@/types/api"

export const EXPENSE_CATEGORIES: { value: ExpenseCategory; label: string; hint: string; placeholder: string }[] = [
  { value: "Electricity", label: "Electricity", hint: "Electricity and generator bills.", placeholder: "e.g. EDL + generator" },
  { value: "Internet", label: "Internet / Wi-Fi", hint: "Internet, Wi-Fi and phone lines.", placeholder: "e.g. Wi-Fi" },
  { value: "Models", label: "Models", hint: "Occasional fees for models, photo shoots and content.", placeholder: "e.g. Photo shoot, autumn collection" },
  { value: "Wages", label: "Wages", hint: "Salaries and wages.", placeholder: "e.g. Diana — October" },
  { value: "Other", label: "Other", hint: "Any other running cost.", placeholder: "What it was for" },
]
const categoryLabel = (c: ExpenseCategory) => EXPENSE_CATEGORIES.find((x) => x.value === c)?.label ?? c

/** "2026-10" for the current month in the business time zone. */
const thisMonth = () => todayIso().slice(0, 7)
const monthRange = (month: string) => {
  const [y, m] = month.split("-").map(Number)
  const last = new Date(Date.UTC(y, m, 0)).getUTCDate()
  return { from: `${month}-01`, to: `${month}-${String(last).padStart(2, "0")}` }
}
const shiftMonth = (month: string, by: number) => {
  const [y, m] = month.split("-").map(Number)
  const d = new Date(Date.UTC(y, m - 1 + by, 1))
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}`
}
const monthLabel = (month: string) =>
  new Intl.DateTimeFormat("en-GB", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-01T00:00:00Z`))

export function BillsPage() {
  const [params, setParams] = useSearchParams()
  const month = /^\d{4}-\d{2}$/.test(params.get("month") ?? "") ? params.get("month")! : thisMonth()
  const category = (params.get("category") ?? "") as ExpenseCategory | ""
  const page = Number(params.get("page") ?? 1)
  const [editing, setEditing] = useState<Expense | "new" | null>(null)
  const [deleting, setDeleting] = useState<Expense | null>(null)
  const queryClient = useQueryClient()

  const set = (patch: Record<string, string>) => setParams((prev) => {
    const next = new URLSearchParams(prev)
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k) }
    if (!("page" in patch)) next.delete("page")
    return next
  }, { replace: true })

  const query = { ...monthRange(month), category: category || undefined, page, pageSize: 50 }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.expenses(query), queryFn: () => api<ExpenseList>("/api/expenses", { query }), placeholderData: keepPreviousData,
  })

  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/expenses/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      setDeleting(null)
      await Promise.all(["finance", "analytics", "dashboard"].map((k) => queryClient.invalidateQueries({ queryKey: [k] })))
      toast.success("Bill deleted")
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  const columns: Column<Expense>[] = [
    { key: "date", header: "Date", cell: (e) => <span className="tabular">{dateOnly(e.incurredOn)}</span> },
    { key: "category", header: "For", cell: (e) => <Pill tone="neutral" dot={false}>{categoryLabel(e.category)}</Pill> },
    {
      key: "description", header: "Description",
      cell: (e) => <div className="min-w-0"><div className="truncate">{e.description ?? <span className="text-muted-foreground">—</span>}</div>
        {e.notes && <div className="truncate text-xs text-muted-foreground">{e.notes}</div>}</div>,
    },
    { key: "by", header: "Recorded by", hideOnMobile: true, cell: (e) => <span className="text-muted-foreground">{e.recordedBy ?? "—"}</span> },
    { key: "amount", header: "Amount", align: "right", cell: (e) => <span className="tabular font-medium">{money(e.amount)}</span> },
    {
      key: "actions", header: "", align: "right",
      cell: (e) => (
        <div className="flex justify-end gap-1">
          <Button size="icon-xs" variant="ghost" aria-label={`Edit ${categoryLabel(e.category)} bill`} onClick={() => setEditing(e)}><Pencil /></Button>
          <Button size="icon-xs" variant="ghost" aria-label={`Delete ${categoryLabel(e.category)} bill`} onClick={() => setDeleting(e)}><Trash2 /></Button>
        </div>
      ),
    },
  ]

  const totalFor = (c: ExpenseCategory) => data?.byCategory.find((x) => x.category === c)?.amount ?? 0
  return (
    <>
      <PageHeader title="Bills & expenses"
        description={<>Running costs such as electricity, Wi-Fi, models and wages. They're subtracted from profit in <Link to="/analytics" className="font-medium text-primary hover:underline">Sales &amp; profit</Link> on the date you give them.</>}
        actions={<Button onClick={() => setEditing("new")}><Plus /> Add bill</Button>} />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-1">
          <Button variant="outline" size="icon-sm" aria-label="Previous month" onClick={() => set({ month: shiftMonth(month, -1) })}><ChevronLeft /></Button>
          <span className="font-display min-w-40 text-center text-xl">{monthLabel(month)}</span>
          <Button variant="outline" size="icon-sm" aria-label="Next month" onClick={() => set({ month: shiftMonth(month, 1) })}><ChevronRight /></Button>
        </div>
        {month !== thisMonth() && <Button variant="ghost" size="sm" onClick={() => set({ month: "" })}>This month</Button>}
        <span className="ml-auto text-sm text-muted-foreground">Total <span className="font-display ml-1 text-2xl text-foreground tabular">{money(data?.total ?? 0)}</span></span>
      </div>

      <div className="mb-4">
        <FigureRow columns={5}>
          {EXPENSE_CATEGORIES.map((c) => (
            <button key={c.value} type="button" title={`${c.hint} Click to show only these bills.`}
              className={`min-w-0 px-3 py-4 text-left transition-colors first:pl-0 hover:bg-accent/40 md:px-5 ${category === c.value ? "bg-blush/50" : ""}`}
              aria-pressed={category === c.value} onClick={() => set({ category: category === c.value ? "" : c.value })}>
              <span className="eyebrow block">{c.label}</span>
              <span className="font-display mt-1 block truncate text-[1.45rem] leading-none tabular md:text-[1.9rem]">{money(totalFor(c.value))}</span>
            </button>
          ))}
        </FigureRow>
      </div>

      {category && (
        <div className="mb-3 flex items-center gap-2 text-sm">
          Showing <span className="font-medium">{categoryLabel(category)}</span> only.
          <Button variant="link" size="sm" className="h-auto px-0" onClick={() => set({ category: "" })}>Show all</Button>
        </div>
      )}

      <DataTable columns={columns} rows={data?.items} rowKey={(e) => e.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={(e) => setEditing(e)}
        empty={<EmptyState title={`No bills for ${monthLabel(month)}`}
          action={<Button size="sm" onClick={() => setEditing("new")}><Plus /> Add bill</Button>}>
          Add this month's electricity, Wi-Fi and wages so profit reflects what the business really earns.
        </EmptyState>}
        footer={data && data.totalCount > data.pageSize && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(p) => set({ page: String(p) })} />} />

      {editing && (
        <BillDialog bill={editing === "new" ? null : editing}
          defaultDate={month === thisMonth() ? todayIso() : monthRange(month).from}
          onClose={() => setEditing(null)} />
      )}
      <ConfirmDialog open={!!deleting} onOpenChange={(o) => !o && setDeleting(null)} destructive pending={remove.isPending}
        title="Delete this bill?" confirmLabel="Delete bill"
        description={deleting && `${categoryLabel(deleting.category)}, ${money(deleting.amount)} on ${dateOnly(deleting.incurredOn)}. Profit figures will no longer include it.`}
        onConfirm={() => deleting && remove.mutate(deleting.id)} />
    </>
  )
}

const amountRe = /^\d{1,9}(\.\d{1,2})?$/

function BillDialog({ bill, defaultDate, onClose }: { bill: Expense | null; defaultDate: string; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [category, setCategory] = useState<ExpenseCategory>(bill?.category ?? "Electricity")
  const [amount, setAmount] = useState(bill ? bill.amount.toFixed(2) : "")
  const [incurredOn, setIncurredOn] = useState(bill?.incurredOn ?? defaultDate)
  const [description, setDescription] = useState(bill?.description ?? "")
  const [notes, setNotes] = useState(bill?.notes ?? "")
  const [submitted, setSubmitted] = useState(false)
  const amountError = !amountRe.test(amount.trim()) || Number(amount) <= 0 ? "Enter an amount, e.g. 45 or 45.50." : undefined
  const dateError = !incurredOn ? "Choose a date." : undefined

  const save = useMutation({
    mutationFn: () => {
      const body = { category, incurredOn, amount: Number(amount), description: description.trim() || null, notes: notes.trim() || null }
      return bill ? api<Expense>(`/api/expenses/${bill.id}`, { method: "PUT", body }) : api<Expense>("/api/expenses", { method: "POST", body })
    },
    onSuccess: async (saved) => {
      await Promise.all(["finance", "analytics", "dashboard"].map((k) => queryClient.invalidateQueries({ queryKey: [k] })))
      toast.success(bill ? "Bill updated" : `${categoryLabel(saved.category)} bill of ${money(saved.amount)} added`)
      onClose()
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : errorMessage(e)),
  })
  const selected = EXPENSE_CATEGORIES.find((c) => c.value === category)!

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{bill ? "Edit bill" : "Add a bill"}</DialogTitle>
          <DialogDescription>It counts against profit on the date below. For a monthly bill, any day in that month works.</DialogDescription>
        </DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); setSubmitted(true); if (!amountError && !dateError) save.mutate() }}>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="For" htmlFor="bill-category" required>
              <Select value={category} onValueChange={(v) => setCategory(v as ExpenseCategory)}>
                <SelectTrigger id="bill-category" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{EXPENSE_CATEGORIES.map((c) => <SelectItem key={c.value} value={c.value}>{c.label}</SelectItem>)}</SelectContent>
              </Select>
            </Field>
            <Field label="Amount (USD)" htmlFor="bill-amount" error={submitted ? amountError : undefined} required>
              <Input id="bill-amount" inputMode="decimal" autoFocus className="tabular" placeholder="0.00" value={amount}
                aria-invalid={submitted && !!amountError} onChange={(e) => setAmount(e.target.value)} />
            </Field>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Date" htmlFor="bill-date" error={submitted ? dateError : undefined} required>
              <Input id="bill-date" type="date" value={incurredOn} onChange={(e) => setIncurredOn(e.target.value)} />
            </Field>
            <Field label="Description" htmlFor="bill-description">
              <Input id="bill-description" maxLength={200} placeholder={selected.placeholder} value={description} onChange={(e) => setDescription(e.target.value)} />
            </Field>
          </div>
          <Field label="Notes" htmlFor="bill-notes">
            <Textarea id="bill-notes" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending}>{bill ? "Save" : "Add bill"}</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
