import { useEffect, useState } from "react"
import { useNavigate, useParams, useSearchParams } from "react-router"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Controller, useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { Field, SubmitButton } from "@/components/common/controls"
import { PageHeader, Section } from "@/components/common/page"
import { ErrorState, FullPageSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, ApiError, errorMessage } from "@/lib/api"
import { P } from "@/lib/permissions"
import { qk, useCategories } from "@/lib/queries"
import type { ProductDetail, ProductType, SizeUnit } from "@/types/api"
import { BundleComponentsEditor, type ComponentLine } from "./BundleComponentsEditor"

const money2 = z.string().trim().regex(/^\d{1,9}(\.\d{1,2})?$/, "Use a number with up to 2 decimals.")
const optional = <T extends z.ZodTypeAny>(s: T) => z.union([z.literal(""), s])

const schema = z.object({
  name: z.string().trim().min(1, "Required.").max(200),
  sku: z.string().trim().regex(/^[A-Za-z0-9][A-Za-z0-9-]{1,79}$/, "2–80 letters, digits or hyphens."),
  slug: optional(z.string().trim().regex(/^[a-z0-9]+(-[a-z0-9]+)*$/, "Lowercase letters, digits and single hyphens.")),
  categoryId: z.string(),
  barcode: z.string().trim().max(100),
  sizeValue: optional(z.string().regex(/^\d{1,6}(\.\d{1,2})?$/, "Enter a number.")),
  sizeUnit: z.enum(["", "Ml", "G", "Pcs"]),
  weightGrams: optional(z.string().regex(/^\d{1,6}$/, "Whole grams.")),
  sellingPrice: money2,
  compareAtPrice: optional(money2),
  shortDescription: z.string().max(1000),
  description: z.string().max(10000),
  howToUse: z.string().max(5000),
  ingredients: z.string().max(5000),
}).refine((v) => !v.compareAtPrice || Number(v.compareAtPrice) >= Number(v.sellingPrice),
  { path: ["compareAtPrice"], message: "Must be at least the selling price." })
  .refine((v) => (v.sizeValue === "") === (v.sizeUnit === ""), { path: ["sizeUnit"], message: "Set both size and unit, or neither." })
type Values = z.infer<typeof schema>

const empty: Values = {
  name: "", sku: "", slug: "", categoryId: "", barcode: "", sizeValue: "", sizeUnit: "", weightGrams: "",
  sellingPrice: "", compareAtPrice: "", shortDescription: "", description: "", howToUse: "", ingredients: "",
}

function toValues(p: ProductDetail): Values {
  return {
    name: p.name, sku: p.sku, slug: p.slug, categoryId: p.categoryId ?? "", barcode: p.barcode ?? "",
    sizeValue: p.sizeValue?.toString() ?? "", sizeUnit: p.sizeUnit ?? "", weightGrams: p.weightGrams?.toString() ?? "",
    sellingPrice: p.sellingPrice.toFixed(2), compareAtPrice: p.compareAtPrice?.toFixed(2) ?? "",
    shortDescription: p.shortDescription ?? "", description: p.description ?? "", howToUse: p.howToUse ?? "", ingredients: p.ingredients ?? "",
  }
}

function toRequest(v: Values) {
  const opt = (s: string) => (s.trim() === "" ? null : s.trim())
  return {
    name: v.name.trim(), sku: v.sku.trim(), slug: opt(v.slug), categoryId: v.categoryId || null, barcode: opt(v.barcode),
    shortDescription: opt(v.shortDescription), description: opt(v.description), howToUse: opt(v.howToUse), ingredients: opt(v.ingredients),
    sizeValue: v.sizeValue ? Number(v.sizeValue) : null, sizeUnit: (v.sizeUnit || null) as SizeUnit | null,
    weightGrams: v.weightGrams ? Number(v.weightGrams) : null,
    sellingPrice: Number(v.sellingPrice), compareAtPrice: v.compareAtPrice ? Number(v.compareAtPrice) : null,
  }
}

export function ProductFormPage() {
  const { id } = useParams()
  const isEdit = !!id
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { can } = useAuth()
  const { data: categories } = useCategories(true)
  const { data: product, isLoading, error } = useQuery({
    queryKey: qk.product(id ?? ""), queryFn: () => api<ProductDetail>(`/api/products/${id}`), enabled: isEdit,
  })
  const type: ProductType = isEdit ? (product?.type ?? "Individual") : params.get("type") === "Bundle" ? "Bundle" : "Individual"
  const [components, setComponents] = useState<ComponentLine[]>([])
  const canPrice = !isEdit || can(P.products.pricing)

  const { register, control, handleSubmit, reset, watch, setError, formState: { errors } } = useForm<Values>({
    resolver: zodResolver(schema), defaultValues: empty,
  })
  useEffect(() => { if (product) reset(toValues(product)) }, [product, reset])

  const save = useMutation({
    mutationFn: (v: Values) => isEdit
      ? api<ProductDetail>(`/api/products/${id}`, { method: "PUT", body: toRequest(v) })
      : api<ProductDetail>("/api/products", {
          method: "POST",
          body: { ...toRequest(v), type, bundleItems: type === "Bundle" ? components.map((c) => ({ componentProductId: c.productId, quantity: c.quantity })) : null },
        }),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ["catalog"] })
      toast.success(isEdit ? "Product saved" : `${saved.name} created`)
      navigate(`/products/${saved.id}`)
    },
    onError: (e) => {
      if (e instanceof ApiError && e.code === "products.duplicate_sku") setError("sku", { message: e.message })
      else if (e instanceof ApiError && e.code === "products.duplicate_slug") setError("slug", { message: e.message })
      else if (e instanceof ApiError && Object.keys(e.fieldErrors).length) {
        for (const [k, msgs] of Object.entries(e.fieldErrors)) setError(k as keyof Values, { message: msgs[0] })
      } else toast.error(errorMessage(e))
    },
  })

  if (isEdit && isLoading) return <FullPageSpinner />
  if (error) return <ErrorState error={error} />

  const onSubmit = handleSubmit((v) => {
    if (!isEdit && type === "Bundle" && components.length === 0) return toast.error("Add at least one product to the bundle.")
    save.mutate(v)
  })
  const title = isEdit ? `Edit ${product?.name}` : type === "Bundle" ? "New bundle" : "New product"

  return (
    <>
      <PageHeader title={title} back={isEdit ? { to: `/products/${id}`, label: product?.name ?? "Product" } : { to: type === "Bundle" ? "/products/bundles" : "/products", label: type === "Bundle" ? "Bundles" : "Products" }} />
      <form onSubmit={onSubmit} noValidate className="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <div className="grid gap-6">
          <Section title="Basics" bodyClassName="grid gap-4 p-4 sm:grid-cols-2">
            <Field label="Name" htmlFor="name" error={errors.name?.message} className="sm:col-span-2" required>
              <Input id="name" {...register("name")} aria-invalid={!!errors.name} />
            </Field>
            <Field label="SKU" htmlFor="sku" error={errors.sku?.message} hint="Stored in capitals, e.g. ALR-HC-MASK." required>
              <Input id="sku" className="font-mono uppercase" {...register("sku")} aria-invalid={!!errors.sku} />
            </Field>
            <Field label="Category" htmlFor="categoryId">
              <Controller control={control} name="categoryId" render={({ field }) => (
                <Select value={field.value || "none"} onValueChange={(v) => field.onChange(v === "none" ? "" : v)}>
                  <SelectTrigger id="categoryId" className="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="none">No category</SelectItem>
                    {categories?.map((c) => <SelectItem key={c.id} value={c.id}>{c.parentName ? `${c.parentName} › ${c.name}` : c.name}</SelectItem>)}
                  </SelectContent>
                </Select>
              )} />
            </Field>
            <Field label="Barcode" htmlFor="barcode" error={errors.barcode?.message}>
              <Input id="barcode" className="font-mono" {...register("barcode")} />
            </Field>
            <Field label="Web address (slug)" htmlFor="slug" error={errors.slug?.message} hint="Leave empty to generate from the name.">
              <Input id="slug" className="font-mono" {...register("slug")} />
            </Field>
          </Section>

          {type === "Bundle" && !isEdit && (
            <Section title="Products in this bundle" bodyClassName="p-4">
              <BundleComponentsEditor value={components} onChange={setComponents} bundlePrice={Number(watch("sellingPrice")) || undefined} />
            </Section>
          )}

          <Section title="Description" bodyClassName="grid gap-4 p-4">
            <Field label="Short description" htmlFor="shortDescription" error={errors.shortDescription?.message}>
              <Textarea id="shortDescription" rows={3} {...register("shortDescription")} />
            </Field>
            <Field label="Full description" htmlFor="description" error={errors.description?.message}>
              <Textarea id="description" rows={5} {...register("description")} />
            </Field>
            {type === "Individual" && (
              <>
                <Field label="How to use" htmlFor="howToUse"><Textarea id="howToUse" rows={3} {...register("howToUse")} /></Field>
                <Field label="Ingredients (INCI)" htmlFor="ingredients"><Textarea id="ingredients" rows={4} {...register("ingredients")} /></Field>
              </>
            )}
          </Section>
        </div>

        <div className="grid content-start gap-6">
          <Section title="Price" bodyClassName="grid gap-4 p-4">
            {!canPrice && <p className="text-xs text-muted-foreground">Your role cannot change prices.</p>}
            <Field label="Selling price (USD)" htmlFor="sellingPrice" error={errors.sellingPrice?.message} required>
              <Input id="sellingPrice" inputMode="decimal" className="tabular" disabled={!canPrice} {...register("sellingPrice")} />
            </Field>
            <Field label="Compare-at price (USD)" htmlFor="compareAtPrice" error={errors.compareAtPrice?.message}
              hint={type === "Bundle" ? "Usually the components' total at full price." : "Shown crossed out when on offer."}>
              <Input id="compareAtPrice" inputMode="decimal" className="tabular" disabled={!canPrice} {...register("compareAtPrice")} />
            </Field>
          </Section>
          {type === "Individual" && (
            <Section title="Size & shipping" bodyClassName="grid grid-cols-2 gap-4 p-4">
              <Field label="Size" htmlFor="sizeValue" error={errors.sizeValue?.message}>
                <Input id="sizeValue" inputMode="decimal" {...register("sizeValue")} />
              </Field>
              <Field label="Unit" htmlFor="sizeUnit" error={errors.sizeUnit?.message}>
                <Controller control={control} name="sizeUnit" render={({ field }) => (
                  <Select value={field.value || "none"} onValueChange={(v) => field.onChange(v === "none" ? "" : v)}>
                    <SelectTrigger id="sizeUnit" className="w-full"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="none">—</SelectItem>
                      <SelectItem value="Ml">ml</SelectItem>
                      <SelectItem value="G">g</SelectItem>
                      <SelectItem value="Pcs">pcs</SelectItem>
                    </SelectContent>
                  </Select>
                )} />
              </Field>
              <Field label="Shipping weight (g)" htmlFor="weightGrams" error={errors.weightGrams?.message} className="col-span-2">
                <Input id="weightGrams" inputMode="numeric" {...register("weightGrams")} />
              </Field>
            </Section>
          )}
          <div className="flex gap-2">
            <SubmitButton pending={save.isPending}>{isEdit ? "Save changes" : type === "Bundle" ? "Create bundle" : "Create product"}</SubmitButton>
            <Button type="button" variant="ghost" onClick={() => navigate(-1)}>Cancel</Button>
          </div>
        </div>
      </form>
    </>
  )
}
