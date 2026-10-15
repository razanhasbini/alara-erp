import { useRef, useState } from "react"
import { Link, useNavigate, useParams } from "react-router"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ImagePlus, Pencil, Star, Trash2 } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { ConfirmDialog } from "@/components/common/controls"
import { Facts, PageHeader, Section } from "@/components/common/page"
import { ActivePill, Pill, StockStatusPill } from "@/components/common/StatusBadge"
import { ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { api, errorMessage } from "@/lib/api"
import { dateOnly, dateTime, money, number } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useAvailability } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { ProductDetail, ProductStock } from "@/types/api"
import { BundleComponentsEditor, type ComponentLine } from "./BundleComponentsEditor"

export function ProductDetailPage() {
  const { id = "" } = useParams()
  const { can } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { data: p, isLoading, error, refetch } = useQuery({ queryKey: qk.product(id), queryFn: () => api<ProductDetail>(`/api/products/${id}`) })
  const isIndividual = p?.type === "Individual"
  const { data: stock } = useQuery({
    queryKey: qk.productStock(id), queryFn: () => api<ProductStock>(`/api/inventory/products/${id}`),
    enabled: isIndividual && can(P.inventory.view),
  })
  const componentIds = p?.bundleItems.map((b) => b.componentProductId) ?? []
  const { data: available } = useAvailability(p ? [p.id, ...componentIds] : [], undefined, !!p && can(P.inventory.view))
  const [confirm, setConfirm] = useState<"delete" | "deactivate" | null>(null)
  const [editComponents, setEditComponents] = useState(false)

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["catalog"] })
  const setActive = useMutation({
    mutationFn: (active: boolean) => api(`/api/products/${id}/${active ? "activate" : "deactivate"}`, { method: "POST" }),
    onSuccess: async (_, active) => { await invalidate(); setConfirm(null); toast.success(active ? "Product activated" : "Product deactivated") },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const remove = useMutation({
    mutationFn: () => api(`/api/products/${id}`, { method: "DELETE" }),
    onSuccess: async () => { await invalidate(); toast.success("Product deleted"); navigate("/products") },
    onError: (e) => { setConfirm(null); toast.error(errorMessage(e)) },
  })

  if (isLoading) return <FullPageSpinner />
  if (error || !p) return <ErrorState error={error} onRetry={refetch} />

  const saving = p.compareAtPrice && p.compareAtPrice > p.sellingPrice ? p.compareAtPrice - p.sellingPrice : 0
  return (
    <>
      <PageHeader
        back={{ to: p.type === "Bundle" ? "/products/bundles" : "/products", label: p.type === "Bundle" ? "Bundles" : "Products" }}
        title={p.name}
        meta={<>
          <span className="code rounded bg-secondary px-1.5 py-0.5">{p.sku}</span>
          <ActivePill active={p.isActive} />
          {p.type === "Bundle" && <Pill tone="rose" dot={false}>Bundle</Pill>}
          {stock && <StockStatusPill status={stock.status} />}
        </>}
        actions={<>
          {can(P.products.update) && (p.isActive
            ? <Button variant="outline" onClick={() => setConfirm("deactivate")}>Deactivate</Button>
            : <Button variant="outline" onClick={() => setActive.mutate(true)} disabled={setActive.isPending}>Activate</Button>)}
          {can(P.products.delete) && <Button variant="ghost" aria-label="Delete product" onClick={() => setConfirm("delete")}><Trash2 /></Button>}
          {can(P.products.update) && <Button asChild><Link to={`/products/${p.id}/edit`}><Pencil /> Edit</Link></Button>}
        </>}
      />

      <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <div className="grid content-start gap-6">
          <Section title="Details" bodyClassName="p-4">
            <Facts items={[
              ["Price", <span key="f77-26" className="tabular">
                <span className="font-medium">{money(p.sellingPrice)}</span>
                {saving > 0 && <span className="ml-2 text-muted-foreground line-through">{money(p.compareAtPrice)}</span>}
                {saving > 0 && <span className="ml-2 text-xs text-rose-ink">save {money(saving)}</span>}
              </span>],
              ["Category", p.categoryName],
              ...(isIndividual ? [
                ["Size", p.sizeValue ? `${number(p.sizeValue)} ${p.sizeUnit?.toLowerCase()}` : <span key="f84-97" className="text-muted-foreground">Not set</span>] as [string, React.ReactNode],
                ["Shipping weight", p.weightGrams ? `${number(p.weightGrams)} g` : <span key="f85-85" className="text-muted-foreground">Not set</span>] as [string, React.ReactNode],
              ] : []),
              ["Barcode", p.barcode ? <span key="f87-40" className="code">{p.barcode}</span> : <span key="f87-84" className="text-muted-foreground">—</span>],
              ["Web address", <span key="f88-32" className="code text-muted-foreground">/product/{p.slug}</span>],
              ["Last updated", dateTime(p.updatedAt)],
            ]} />
          </Section>

          {p.type === "Bundle" && (
            <Section title="In this bundle" actions={can(P.products.update) && <Button size="sm" variant="outline" onClick={() => setEditComponents(true)}>Edit contents</Button>}>
              <table className="w-full text-sm">
                <tbody>
                  {p.bundleItems.map((b) => (
                    <tr key={b.componentProductId} className="border-b border-hairline last:border-0">
                      <td className="px-4 py-2.5">
                        <Link to={`/products/${b.componentProductId}`} className="hover:underline">{b.name}</Link>
                        <div className="code text-muted-foreground">{b.sku}{!b.isActive && " · inactive"}</div>
                      </td>
                      <td className="px-4 py-2.5 text-right tabular">× {b.quantity}</td>
                      <td className="px-4 py-2.5 text-right tabular text-muted-foreground">{money(b.unitPrice * b.quantity)}</td>
                      {available && <td className="px-4 py-2.5 text-right tabular text-muted-foreground">{available[b.componentProductId] ?? 0} in stock</td>}
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr className="border-t border-hairline bg-secondary/40 text-sm">
                    <td className="px-4 py-2.5 font-medium">Bought separately</td>
                    <td />
                    <td className="px-4 py-2.5 text-right tabular font-medium">{money(p.componentsValue)}</td>
                    {available && <td className="px-4 py-2.5 text-right tabular font-medium">{available[p.id] ?? 0} sets</td>}
                  </tr>
                </tfoot>
              </table>
            </Section>
          )}

          {(p.shortDescription || p.description || p.howToUse || p.ingredients) && (
            <Section title="Description" bodyClassName="grid gap-4 p-4 text-sm leading-relaxed">
              {p.shortDescription && <p className="font-medium">{p.shortDescription}</p>}
              {p.description && <p className="whitespace-pre-line text-muted-foreground">{p.description}</p>}
              {p.howToUse && <div><p className="eyebrow mb-1">How to use</p><p className="whitespace-pre-line">{p.howToUse}</p></div>}
              {p.ingredients && <div><p className="eyebrow mb-1">Ingredients</p><p className="text-xs leading-relaxed text-muted-foreground">{p.ingredients}</p></div>}
            </Section>
          )}
        </div>

        <div className="grid content-start gap-6">
          <ImagesSection product={p} canEdit={can(P.products.update)} />
          {stock && (
            <Section title="Stock" actions={<Button asChild size="sm" variant="ghost"><Link to={`/inventory?product=${p.id}`}>Open</Link></Button>}
              bodyClassName="p-4">
              <div className="grid grid-cols-3 gap-2 text-center">
                {[["On hand", stock.onHand], ["Reserved", stock.reserved], ["Available", stock.available]].map(([l, v]) => (
                  <div key={l as string}><div className="font-display text-2xl tabular">{v}</div><div className="eyebrow">{l}</div></div>
                ))}
              </div>
              <p className="mt-3 text-xs text-muted-foreground">
                Low-stock alert at {stock.settings.lowStockThreshold} · {stock.settings.tracksExpiry ? "tracks expiry dates" : "no expiry tracking"}
              </p>
              {stock.batches.length > 0 && (
                <ul className="mt-3 divide-y divide-hairline border-t border-hairline text-xs">
                  {stock.batches.map((b) => (
                    <li key={`${b.batchId}-${b.warehouseId}`} className="flex justify-between py-1.5">
                      <span><span className="code">{b.batchNumber}</span> <span className="text-muted-foreground">· {b.warehouseCode}</span></span>
                      <span className={cn("tabular", b.isExpired && "text-tone-red")}>{b.onHand} · {b.expiryDate ? dateOnly(b.expiryDate) : "no expiry"}</span>
                    </li>
                  ))}
                </ul>
              )}
            </Section>
          )}
        </div>
      </div>

      <ConfirmDialog open={confirm === "deactivate"} onOpenChange={(o) => !o && setConfirm(null)}
        title={`Deactivate ${p.name}?`} confirmLabel="Deactivate" pending={setActive.isPending} onConfirm={() => setActive.mutate(false)}
        description="It will no longer be offered in new orders or bundles. Existing orders and stock are not affected." />
      <ConfirmDialog open={confirm === "delete"} onOpenChange={(o) => !o && setConfirm(null)} destructive
        title={`Delete ${p.name}?`} confirmLabel="Delete permanently" pending={remove.isPending} onConfirm={() => remove.mutate()}
        description="Only products that were never stocked, sold or bundled can be deleted. Otherwise deactivate it instead." />
      {editComponents && (
        <ComponentsDialog product={p} onClose={() => setEditComponents(false)} />
      )}
    </>
  )
}

function ComponentsDialog({ product, onClose }: { product: ProductDetail; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [lines, setLines] = useState<ComponentLine[]>(product.bundleItems.map((b) => ({
    productId: b.componentProductId, name: b.name, sku: b.sku, unitPrice: b.unitPrice, quantity: b.quantity,
  })))
  const save = useMutation({
    mutationFn: () => api(`/api/products/${product.id}/bundle-items`, {
      method: "PUT", body: { items: lines.map((l) => ({ componentProductId: l.productId, quantity: l.quantity })) },
    }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: ["catalog"] }); toast.success("Bundle contents saved"); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader><DialogTitle>Contents of {product.name}</DialogTitle></DialogHeader>
        <p className="text-xs text-muted-foreground">Orders already placed keep the contents they were sold with.</p>
        <BundleComponentsEditor value={lines} onChange={setLines} bundlePrice={product.sellingPrice} />
        <DialogFooter>
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button onClick={() => save.mutate()} disabled={save.isPending || lines.length === 0}>{save.isPending && <InlineSpinner />} Save contents</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function ImagesSection({ product, canEdit }: { product: ProductDetail; canEdit: boolean }) {
  const queryClient = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["catalog"] })
  const upload = useMutation({
    mutationFn: async (files: FileList) => {
      for (const file of Array.from(files)) {
        if (file.size > 5 * 1024 * 1024) throw new Error(`${file.name} is larger than 5 MB.`)
        const body = new FormData()
        body.append("file", file)
        await api(`/api/products/${product.id}/images`, { method: "POST", body })
      }
    },
    onSuccess: async () => { await refresh(); toast.success("Image uploaded") },
    onError: async (e) => { await refresh(); toast.error(errorMessage(e)) },
  })
  const primary = useMutation({
    mutationFn: (imageId: string) => api(`/api/products/${product.id}/images/${imageId}/primary`, { method: "PUT" }),
    onSuccess: refresh, onError: (e) => toast.error(errorMessage(e)),
  })
  const remove = useMutation({
    mutationFn: (imageId: string) => api(`/api/products/${product.id}/images/${imageId}`, { method: "DELETE" }),
    onSuccess: refresh, onError: (e) => toast.error(errorMessage(e)),
  })
  const images = [...product.images].sort((a, b) => a.displayOrder - b.displayOrder)

  return (
    <Section title="Images" actions={canEdit && (
      <>
        <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" multiple hidden
          onChange={(e) => { if (e.target.files?.length) upload.mutate(e.target.files); e.target.value = "" }} />
        <Button size="sm" variant="outline" onClick={() => input.current?.click()} disabled={upload.isPending}>
          {upload.isPending ? <InlineSpinner /> : <ImagePlus />} Upload
        </Button>
      </>
    )} bodyClassName="p-3">
      {images.length === 0 ? (
        <p className="px-1 py-6 text-center text-xs text-muted-foreground">No images yet. JPEG, PNG or WebP up to 5 MB.</p>
      ) : (
        <ul className="grid grid-cols-2 gap-2">
          {images.map((img) => (
            <li key={img.id} className={cn("group relative overflow-hidden rounded-md border bg-white", img.isPrimary ? "border-rose" : "border-hairline")}>
              <img src={`/api/products/${product.id}/images/${img.id}`} alt={`${product.name} image`} className="aspect-square w-full object-cover" />
              {img.isPrimary && <span className="absolute top-1.5 left-1.5 rounded bg-white/90 px-1.5 py-0.5 text-[0.65rem] font-medium text-rose-ink">Main</span>}
              {canEdit && (
                <div className="absolute inset-x-1.5 bottom-1.5 flex justify-end gap-1 opacity-0 transition-opacity group-focus-within:opacity-100 group-hover:opacity-100">
                  {!img.isPrimary && (
                    <Button size="icon-xs" variant="secondary" aria-label="Make main image" onClick={() => primary.mutate(img.id)}><Star /></Button>
                  )}
                  <Button size="icon-xs" variant="secondary" aria-label="Delete image" onClick={() => remove.mutate(img.id)}><Trash2 /></Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </Section>
  )
}
