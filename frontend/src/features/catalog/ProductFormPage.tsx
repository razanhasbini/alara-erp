import { useEffect, useRef, useState } from "react"
import { useNavigate, useParams, useSearchParams } from "react-router"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Controller, useForm, useWatch } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { ImagePlus, X } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { Field, SubmitButton } from "@/components/common/controls"
import { PageHeader, Section } from "@/components/common/page"
import { ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { api, ApiError, errorMessage } from "@/lib/api"
import { money } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useCategories } from "@/lib/queries"
import type { ProductDetail, ProductType, SizeUnit } from "@/types/api"
import { BundleComponentsEditor, type ComponentLine } from "./BundleComponentsEditor"

const money2 = z.string().trim().regex(/^\d{1,9}(\.\d{1,2})?$/, "Use a number with up to 2 decimals.")
const optional = <T extends z.ZodTypeAny>(s: T) => z.union([z.literal(""), s])

const schema = z.object({
  name: z.string().trim().min(1, "Required.").max(200),
  slug: optional(z.string().trim().regex(/^[a-z0-9]+(-[a-z0-9]+)*$/, "Lowercase letters, digits and single hyphens.")),
  categoryId: z.string(),
  sizeValue: optional(z.string().regex(/^\d{1,6}(\.\d{1,2})?$/, "Enter a number.")),
  sizeUnit: z.enum(["", "Ml", "G", "Pcs"]),
  weightGrams: optional(z.string().regex(/^\d{1,6}$/, "Whole grams.")),
  sellingPrice: money2,
  compareAtPrice: optional(money2),
  standardCost: optional(z.string().trim().regex(/^\d{1,8}(\.\d{1,4})?$/, "Use a number with up to 4 decimals.")),
  shortDescription: z.string().max(1000),
  description: z.string().max(10000),
  howToUse: z.string().max(5000),
  ingredients: z.string().max(5000),
}).refine((v) => !v.compareAtPrice || Number(v.compareAtPrice) >= Number(v.sellingPrice),
  { path: ["compareAtPrice"], message: "Must be at least the selling price." })
  .refine((v) => (v.sizeValue === "") === (v.sizeUnit === ""), { path: ["sizeUnit"], message: "Set both size and unit, or neither." })
type Values = z.infer<typeof schema>

const empty: Values = {
  name: "", slug: "", categoryId: "", sizeValue: "", sizeUnit: "", weightGrams: "",
  sellingPrice: "", compareAtPrice: "", standardCost: "", shortDescription: "", description: "", howToUse: "", ingredients: "",
}

function toValues(p: ProductDetail): Values {
  return {
    name: p.name, slug: p.slug, categoryId: p.categoryId ?? "",
    sizeValue: p.sizeValue?.toString() ?? "", sizeUnit: p.sizeUnit ?? "", weightGrams: p.weightGrams?.toString() ?? "",
    sellingPrice: p.sellingPrice.toFixed(2), compareAtPrice: p.compareAtPrice?.toFixed(2) ?? "",
    standardCost: p.standardCost?.toString() ?? "",
    shortDescription: p.shortDescription ?? "", description: p.description ?? "", howToUse: p.howToUse ?? "", ingredients: p.ingredients ?? "",
  }
}

function toRequest(v: Values, includeCost: boolean) {
  const opt = (s: string) => (s.trim() === "" ? null : s.trim())
  return {
    name: v.name.trim(), slug: opt(v.slug), categoryId: v.categoryId || null,
    shortDescription: opt(v.shortDescription), description: opt(v.description), howToUse: opt(v.howToUse), ingredients: opt(v.ingredients),
    sizeValue: v.sizeValue ? Number(v.sizeValue) : null, sizeUnit: (v.sizeUnit || null) as SizeUnit | null,
    weightGrams: v.weightGrams ? Number(v.weightGrams) : null,
    sellingPrice: Number(v.sellingPrice), compareAtPrice: v.compareAtPrice ? Number(v.compareAtPrice) : null,
    ...(includeCost ? { standardCost: v.standardCost ? Number(v.standardCost) : null } : {}),
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
  const [pendingImages, setPendingImages] = useState<File[]>([])
  const imageInput = useRef<HTMLInputElement>(null)
  const canPrice = !isEdit || can(P.products.pricing)
  const showCost = can(P.costs.view)

  const { register, control, handleSubmit, reset, setError, formState: { errors } } = useForm<Values>({
    resolver: zodResolver(schema), defaultValues: empty,
  })
  const [sellingPrice, standardCost, compareAtPrice] = useWatch({ control, name: ["sellingPrice", "standardCost", "compareAtPrice"] })
  useEffect(() => { if (product) reset(toValues(product)) }, [product, reset])

  const uploadImages = useMutation({
    mutationFn: async (files: File[]) => {
      for (const file of files) {
        const body = new FormData()
        body.append("file", file)
        await api(`/api/products/${id}/images`, { method: "POST", body })
      }
    },
    onSuccess: async (_, files) => {
      await queryClient.invalidateQueries({ queryKey: ["catalog"] })
      setPendingImages((current) => current.filter((file) => !files.includes(file)))
      toast.success(files.length === 1 ? "Photo uploaded and now visible" : `${files.length} photos uploaded and now visible`)
    },
    onError: (e, files) => {
      setPendingImages((current) => current.filter((file) => !files.includes(file)))
      toast.error(errorMessage(e))
    },
  })

  const save = useMutation({
    mutationFn: async (v: Values) => {
      const saved = isEdit
        ? await api<ProductDetail>(`/api/products/${id}`, { method: "PUT", body: toRequest(v, showCost && type === "Individual") })
        : await api<ProductDetail>("/api/products", {
          method: "POST",
          body: { ...toRequest(v, showCost && type === "Individual"), type, bundleItems: type === "Bundle" ? components.map((c) => ({ componentProductId: c.productId, quantity: c.quantity })) : null },
        })

      let imageError: string | null = null
      for (const file of isEdit ? [] : pendingImages) {
        try {
          const body = new FormData()
          body.append("file", file)
          await api(`/api/products/${saved.id}/images`, { method: "POST", body })
        } catch (e) {
          imageError = errorMessage(e)
          break
        }
      }
      return { saved, imageError }
    },
    onSuccess: async ({ saved, imageError }) => {
      await queryClient.invalidateQueries({ queryKey: ["catalog"] })
      if (imageError) toast.warning(`${saved.name} was saved, but a photo could not be uploaded: ${imageError}`)
      else toast.success(isEdit ? "Product saved" : `${saved.name} created`)
      navigate(`/products/${saved.id}`)
    },
    onError: (e) => {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length) {
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
  const addImages = (files: FileList) => {
    const chosen = Array.from(files)
    const invalid = chosen.find((file) => !["image/jpeg", "image/png", "image/webp"].includes(file.type) || file.size > 5 * 1024 * 1024)
    if (invalid) return toast.error(`${invalid.name} must be a JPEG, PNG or WebP image no larger than 5 MB.`)
    const remaining = Math.max(0, 12 - (product?.images.length ?? 0) - pendingImages.length)
    if (chosen.length > remaining) return toast.error(`You can add ${remaining} more photo${remaining === 1 ? "" : "s"}.`)
    setPendingImages((current) => [...current, ...chosen])
    if (isEdit) uploadImages.mutate(chosen)
  }
  const title = isEdit ? `Edit ${product?.name}` : type === "Bundle" ? "New bundle" : "New product"

  return (
    <>
      <PageHeader title={title} back={isEdit ? { to: `/products/${id}`, label: product?.name ?? "Product" } : { to: type === "Bundle" ? "/products/bundles" : "/products", label: type === "Bundle" ? "Bundles" : "Products" }} />
      <form onSubmit={onSubmit} noValidate className="grid gap-6 lg:grid-cols-[1fr_22rem]">
        <div className="grid gap-6">
          <Section title="Basics" bodyClassName="grid gap-4 p-4 sm:grid-cols-2">
            <Field label="Name" htmlFor="name" error={errors.name?.message} required>
              <Input id="name" {...register("name")} aria-invalid={!!errors.name} />
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
          </Section>

          {type === "Bundle" && !isEdit && (
            <Section title="Products in this bundle" bodyClassName="p-4">
              <BundleComponentsEditor value={components} onChange={setComponents} bundlePrice={Number(sellingPrice) || undefined} compareAtPrice={Number(compareAtPrice) || undefined} />
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
          <Section title="Photos" bodyClassName="grid gap-3 p-4">
            <input ref={imageInput} type="file" accept="image/jpeg,image/png,image/webp" multiple hidden
              onChange={(e) => { if (e.target.files?.length) addImages(e.target.files); e.target.value = "" }} />
            {(product?.images.length || pendingImages.length) ? (
              <div className="grid grid-cols-3 gap-2">
                {[...(product?.images ?? [])].sort((a, b) => a.displayOrder - b.displayOrder).map((image) => (
                  <div key={image.id} className="relative overflow-hidden rounded-md border border-hairline bg-white">
                    <img src={`/api/products/${id}/images/${image.id}`} alt={`${product?.name ?? "Product"} photo`} className="aspect-square w-full object-cover" />
                    {image.isPrimary && <span className="absolute top-1 left-1 rounded bg-white/90 px-1.5 py-0.5 text-[0.65rem] font-medium text-rose-ink">Main</span>}
                  </div>
                ))}
                {pendingImages.map((file) => (
                  <PendingPhoto key={`${file.name}-${file.size}-${file.lastModified}`} file={file}
                    onRemove={() => setPendingImages((current) => current.filter((item) => item !== file))} />
                ))}
              </div>
            ) : <p className="text-sm text-muted-foreground">Add a clear product photo. The first photo becomes the main image in the catalogue and order picker.</p>}
            <Button type="button" variant="outline" onClick={() => imageInput.current?.click()} disabled={save.isPending || uploadImages.isPending}>
              {uploadImages.isPending ? <InlineSpinner /> : <ImagePlus />} {uploadImages.isPending ? "Uploading…" : pendingImages.length || product?.images.length ? "Add another photo" : "Choose photo"}
            </Button>
            {pendingImages.length > 0 && <p className="text-xs text-muted-foreground">{isEdit ? "Uploading now…" : `The selected photo${pendingImages.length === 1 ? "" : "s"} will upload when you create this product.`}</p>}
          </Section>
          <Section title="Price" bodyClassName="grid gap-4 p-4">
            {!canPrice && <p className="text-xs text-muted-foreground">Your role cannot change prices.</p>}
            <Field label="Selling price (USD)" htmlFor="sellingPrice" error={errors.sellingPrice?.message} required>
              <Input id="sellingPrice" inputMode="decimal" className="tabular" disabled={!canPrice} {...register("sellingPrice")} />
            </Field>
            <Field label="Compare-at price (USD)" htmlFor="compareAtPrice" error={errors.compareAtPrice?.message}
              hint={type === "Bundle" ? "Shown crossed out. Leave empty to compare against the products bought separately." : "Shown crossed out when on offer."}>
              <Input id="compareAtPrice" inputMode="decimal" className="tabular" disabled={!canPrice} {...register("compareAtPrice")} />
            </Field>
          </Section>
          {showCost && type === "Individual" && (
            <Section title="Cost" bodyClassName="grid gap-4 p-4">
              <Field label="Unit cost (USD)" htmlFor="standardCost" error={errors.standardCost?.message}
                hint="What one unit costs you, including shipping and customs. Used automatically when you receive stock, so you only type a cost there if a delivery costs something different.">
                <Input id="standardCost" inputMode="decimal" className="tabular" disabled={!canPrice} {...register("standardCost")} />
              </Field>
              {Number(sellingPrice) > 0 && standardCost && (
                <p className="text-xs text-muted-foreground">You earn <span className="tabular font-medium text-foreground">{money(Number(sellingPrice) - Number(standardCost))}</span> per unit
                  ({Math.round(((Number(sellingPrice) - Number(standardCost)) / Number(sellingPrice)) * 100)}% margin).</p>
              )}
              <p className="text-xs text-muted-foreground">Only people allowed to see costs see this section. Changing it affects future deliveries, not stock already received.</p>
            </Section>
          )}
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
            <SubmitButton pending={save.isPending} disabled={uploadImages.isPending}>{isEdit ? "Save changes" : type === "Bundle" ? "Create bundle" : "Create product"}</SubmitButton>
            <Button type="button" variant="ghost" onClick={() => navigate(-1)}>Cancel</Button>
          </div>
        </div>
      </form>
    </>
  )
}

function PendingPhoto({ file, onRemove }: { file: File; onRemove: () => void }) {
  // Created inside the effect so a remount (React StrictMode mounts twice in development) never uses a revoked URL.
  const [src, setSrc] = useState<string>()
  useEffect(() => {
    const url = URL.createObjectURL(file)
    setSrc(url)
    return () => URL.revokeObjectURL(url)
  }, [file])
  return (
    <div className="group relative overflow-hidden rounded-md border border-rose bg-white">
      {src ? <img src={src} alt={`Preview of ${file.name}`} className="aspect-square w-full object-cover" /> : <div className="aspect-square w-full bg-secondary" />}
      <span className="absolute top-1 left-1 rounded bg-white/90 px-1.5 py-0.5 text-[0.65rem] font-medium text-rose-ink">New</span>
      <Button type="button" size="icon-xs" variant="secondary" aria-label={`Remove ${file.name}`}
        className="absolute right-1 bottom-1" onClick={onRemove}><X /></Button>
    </div>
  )
}
