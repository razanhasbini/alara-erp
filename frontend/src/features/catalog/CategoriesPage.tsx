import { useState } from "react"
import { Link } from "react-router"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { Controller, useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { CornerDownRight, Plus } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { DataTable, type Column } from "@/components/common/DataTable"
import { Field, SubmitButton } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { ActivePill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { api, ApiError, errorMessage } from "@/lib/api"
import { P } from "@/lib/permissions"
import { useCategories } from "@/lib/queries"
import type { Category } from "@/types/api"

const schema = z.object({
  name: z.string().trim().min(1, "Required.").max(100),
  parentCategoryId: z.string(),
  description: z.string().max(2000),
  sortOrder: z.string().regex(/^\d{1,4}$/, "0–9999."),
  isActive: z.boolean(),
})
type Values = z.infer<typeof schema>

export function CategoriesPage() {
  const { can } = useAuth()
  const { data, isLoading, error, refetch } = useCategories(true)
  const [editing, setEditing] = useState<Category | "new" | null>(null)
  const canEdit = can(P.products.update)

  const columns: Column<Category>[] = [
    {
      key: "name", header: "Category",
      cell: (c) => (
        <span className={c.parentCategoryId ? "flex items-center gap-1.5 pl-5" : "font-medium"}>
          {c.parentCategoryId && <CornerDownRight className="size-3.5 text-muted-foreground" />}{c.name}
        </span>
      ),
    },
    { key: "description", header: "Description", hideOnMobile: true, cell: (c) => <span className="line-clamp-1 text-muted-foreground">{c.description ?? "—"}</span> },
    { key: "products", header: "Products", align: "right", cell: (c) => <Link className="tabular hover:underline" to={`/products?category=${c.id}&status=all`} onClick={(e) => e.stopPropagation()}>{c.productCount}</Link> },
    { key: "status", header: "Status", cell: (c) => <ActivePill active={c.isActive} /> },
  ]

  return (
    <>
      <PageHeader title="Categories" description="How products are grouped. A category can have sub-categories (e.g. Hair Care › Curls On Collection)."
        actions={can(P.products.create) && <Button onClick={() => setEditing("new")}><Plus /> New category</Button>} />
      <DataTable columns={columns} rows={data} rowKey={(c) => c.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={canEdit ? (c) => setEditing(c) : undefined}
        empty={<EmptyState title="No categories yet" />} />
      {editing && <CategoryDialog category={editing === "new" ? null : editing} all={data ?? []} onClose={() => setEditing(null)} />}
    </>
  )
}

function CategoryDialog({ category, all, onClose }: { category: Category | null; all: Category[]; onClose: () => void }) {
  const queryClient = useQueryClient()
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: category?.name ?? "", parentCategoryId: category?.parentCategoryId ?? "", description: category?.description ?? "",
      sortOrder: String(category?.sortOrder ?? 0), isActive: category?.isActive ?? true,
    },
  })
  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = { name: v.name, slug: category?.slug ?? null, description: v.description || null,
        parentCategoryId: v.parentCategoryId || null, sortOrder: Number(v.sortOrder), isActive: v.isActive }
      return category ? api(`/api/categories/${category.id}`, { method: "PUT", body }) : api("/api/categories", { method: "POST", body })
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["catalog"] }); toast.success("Category saved"); onClose() },
    onError: (e) => {
      if (e instanceof ApiError && e.code.startsWith("categories.")) setError("parentCategoryId", { message: e.message })
      else toast.error(errorMessage(e))
    },
  })
  const parents = all.filter((c) => c.id !== category?.id)

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{category ? `Edit ${category.name}` : "New category"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit((v) => save.mutate(v))} noValidate className="grid gap-4">
          <Field label="Name" htmlFor="cat-name" error={errors.name?.message} required><Input id="cat-name" {...register("name")} /></Field>
          <Field label="Parent category" htmlFor="cat-parent" error={errors.parentCategoryId?.message}>
            <Controller control={control} name="parentCategoryId" render={({ field }) => (
              <Select value={field.value || "none"} onValueChange={(v) => field.onChange(v === "none" ? "" : v)}>
                <SelectTrigger id="cat-parent" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">None (top level)</SelectItem>
                  {parents.map((c) => <SelectItem key={c.id} value={c.id}>{c.parentName ? `${c.parentName} › ${c.name}` : c.name}</SelectItem>)}
                </SelectContent>
              </Select>
            )} />
          </Field>
          <Field label="Description" htmlFor="cat-desc"><Textarea id="cat-desc" rows={2} {...register("description")} /></Field>
          <div className="grid grid-cols-2 items-end gap-4">
            <Field label="Sort order" htmlFor="cat-sort" error={errors.sortOrder?.message}><Input id="cat-sort" inputMode="numeric" {...register("sortOrder")} /></Field>
            <Controller control={control} name="isActive" render={({ field }) => (
              <label className="flex h-8 items-center gap-2 text-sm"><Switch checked={field.value} onCheckedChange={field.onChange} /> Active</label>
            )} />
          </div>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={onClose}>Cancel</Button>
            <SubmitButton pending={save.isPending}>Save</SubmitButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
