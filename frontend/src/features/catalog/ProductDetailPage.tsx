import { useEffect, useRef, useState } from "react"
import { Link, useNavigate, useParams } from "react-router"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowLeft, ArrowRight, ImagePlus, PackageMinus, Pencil, RefreshCw, Star, Trash2 } from "lucide-react"
import { toast } from "sonner"
import { useAuth } from "@/auth/auth"
import { ConfirmDialog } from "@/components/common/controls"
import { Facts, PageHeader, Section } from "@/components/common/page"
import { ActivePill, Pill, StockStatusPill } from "@/components/common/StatusBadge"
import { ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { api, errorMessage } from "@/lib/api"
import { cost as fmtCost, date, dateOnly, dateTime, money, number, percent, todayIso } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useAvailability, useSettings } from "@/lib/queries"
import { cn } from "@/lib/utils"
import { deliveryLabel } from "@/features/inventory/deliveryLabel"
import type { ProductAnalytics, ProductDetail, ProductStock } from "@/types/api"
import { WithdrawBundleDialog } from "./WithdrawBundleDialog"
import { BundleComponentsEditor, type ComponentLine } from "./BundleComponentsEditor"

export function ProductDetailPage() {
  const { id = "" } = useParams()
  const { can } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { data: settings } = useSettings()
  const expiryEnabled = settings?.expiryDatesEnabled ?? false
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
  const [withdraw, setWithdraw] = useState(false)
  // Today's set cost and margin for bundles (cost-viewers only).
  const { data: costing } = useQuery({
    queryKey: ["analytics", "costing"],
    queryFn: () => api<ProductAnalytics>("/api/analytics/products", { query: { from: todayIso(), to: todayIso() } }),
    enabled: p?.type === "Bundle" && can(P.costs.view) && can(P.reports.view),
    staleTime: 60_000,
  })
  const bundleCosting = costing?.bundles.find((b) => b.productId === id)

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
          <ActivePill active={p.isActive} />
          {p.type === "Bundle" && <Pill tone="rose" dot={false}>Bundle</Pill>}
          {stock && <StockStatusPill status={stock.status} />}
        </>}
        actions={<>
          {can(P.products.update) && (p.isActive
            ? <Button variant="outline" onClick={() => setConfirm("deactivate")}>Deactivate</Button>
            : <Button variant="outline" onClick={() => setActive.mutate(true)} disabled={setActive.isPending}>Activate</Button>)}
          {p.type === "Bundle" && p.isActive && can(P.inventory.adjust) && (
            <Button variant="outline" onClick={() => setWithdraw(true)}><PackageMinus /> Take out of stock</Button>
          )}
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
              ["Last updated", dateTime(p.updatedAt)],
            ]} />
          </Section>

          {p.type === "Bundle" && (
            <Section title="In this bundle — selling one deducts these" actions={can(P.products.update) && <Button size="sm" variant="outline" onClick={() => setEditComponents(true)}>Edit contents</Button>}>
              <table className="w-full text-sm">
                <tbody>
                  {p.bundleItems.map((b) => (
                    <tr key={b.componentProductId} className="border-b border-hairline last:border-0">
                      <td className="px-4 py-2.5">
                        <Link to={`/products/${b.componentProductId}`} className="hover:underline">{b.name}</Link>
                        {!b.isActive && <div className="text-xs text-muted-foreground">Inactive</div>}
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
          {stock?.cost && (
            <Section title="Cost & margin" bodyClassName="p-4">
              {(() => {
                const unitCost = stock.cost.averageCost
                const earn = unitCost !== null ? p.sellingPrice - unitCost : null
                return (
                  <>
                    <div className="grid grid-cols-2 gap-2 border-b border-hairline pb-3 text-center">
                      <div><div className="font-display text-2xl tabular">{fmtCost(unitCost)}</div><div className="eyebrow">Unit cost</div></div>
                      <div><div className={cn("font-display text-2xl tabular", earn !== null && earn < 0 && "text-tone-red")}>{earn === null ? "—" : money(earn)}</div>
                        <div className="eyebrow">You earn · {earn === null || p.sellingPrice <= 0 ? "—" : percent((earn / p.sellingPrice) * 100, 0)}</div></div>
                    </div>
                    <Facts className="mt-3 text-xs" items={[
                      ["Standard cost", fmtCost(stock.cost.standardCost)],
                      ["Last invoice", stock.cost.lastReceiptCost !== null ? `${fmtCost(stock.cost.lastReceiptCost)} · ${date(stock.cost.lastReceivedAt)}` : "—"],
                      ["Stock value", money(stock.cost.stockValue)],
                    ]} />
                    {unitCost === null && <p className="mt-2 text-xs text-tone-amber">No cost recorded yet — set the unit cost when editing the product or receiving stock.</p>}
                    {stock.cost.uncostedUnits > 0 && <p className="mt-2 text-xs text-tone-amber">{stock.cost.uncostedUnits} unit(s) in stock have no cost.</p>}
                  </>
                )
              })()}
            </Section>
          )}
          {p.type === "Bundle" && bundleCosting && (
            <Section title="Cost & margin" bodyClassName="p-4">
              <div className="grid grid-cols-2 gap-2 text-center">
                <div><div className="font-display text-2xl tabular">{money(bundleCosting.componentsCost)}</div><div className="eyebrow">Set cost</div></div>
                <div><div className={cn("font-display text-2xl tabular", (bundleCosting.unitMargin ?? 0) < 0 && "text-tone-red")}>{money(bundleCosting.unitMargin)}</div>
                  <div className="eyebrow">You earn · {percent(bundleCosting.unitMarginPercent, 0)}</div></div>
              </div>
              {bundleCosting.componentsCost === null && <p className="mt-2 text-xs text-tone-amber">Some products in this bundle have no cost yet.</p>}
            </Section>
          )}
          {stock && (
            <Section title="Stock" actions={<Button asChild size="sm" variant="ghost"><Link to={`/inventory?product=${p.id}`}>Open</Link></Button>}
              bodyClassName="p-4">
              <div className="grid grid-cols-3 gap-2 text-center">
                {[["In stock", stock.onHand], ["Reserved", stock.reserved], ["Available", stock.available]].map(([l, v]) => (
                  <div key={l as string}><div className="font-display text-2xl tabular">{v}</div><div className="eyebrow">{l}</div></div>
                ))}
              </div>
              <p className="mt-3 text-xs text-muted-foreground">
                Low-stock alert at {stock.settings.lowStockThreshold}{expiryEnabled && <> · {stock.settings.tracksExpiry ? "tracks expiry dates" : "no expiry tracking"}</>}
              </p>
              {stock.batches.length > 0 && (
                <ul className="mt-3 divide-y divide-hairline border-t border-hairline text-xs">
                  {stock.batches.map((b) => (
                    <li key={`${b.batchId}-${b.warehouseId}`} className="flex justify-between py-1.5">
                      <span>{deliveryLabel(b, expiryEnabled)}{new Set(stock.batches.map((x) => x.warehouseId)).size > 1 && <span className="text-muted-foreground"> · {b.warehouseCode}</span>}</span>
                      <span className={cn("tabular", expiryEnabled && b.isExpired && "text-tone-red")}>{b.onHand}{expiryEnabled && <> · {b.expiryDate ? dateOnly(b.expiryDate) : "no expiry"}</>}</span>
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
      {withdraw && <WithdrawBundleDialog bundle={p} onClose={() => setWithdraw(false)} />}
    </>
  )
}

function ComponentsDialog({ product, onClose }: { product: ProductDetail; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [lines, setLines] = useState<ComponentLine[]>(product.bundleItems.map((b) => ({
    productId: b.componentProductId, name: b.name, unitPrice: b.unitPrice, quantity: b.quantity,
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
  const replaceInput = useRef<HTMLInputElement>(null)
  const replaceTarget = useRef<string | null>(null)
  const [pendingImages, setPendingImages] = useState<File[]>([])
  const [replacing, setReplacing] = useState<string | null>(null)
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["catalog"] })
  const tooBig = (file: File) => { if (file.size > 5 * 1024 * 1024) throw new Error(`${file.name} is larger than 5 MB.`) }

  const upload = useMutation({
    mutationFn: async (files: File[]) => {
      for (const file of files) {
        tooBig(file)
        const body = new FormData()
        body.append("file", file)
        await api(`/api/products/${product.id}/images`, { method: "POST", body })
      }
    },
    onSuccess: async (_, files) => { await refresh(); setPendingImages([]); toast.success(files.length === 1 ? "Photo uploaded and now visible" : `${files.length} photos uploaded and now visible`) },
    onError: async (e) => { await refresh(); setPendingImages([]); toast.error(errorMessage(e)) },
  })
  const replace = useMutation({
    mutationFn: async ({ imageId, file }: { imageId: string; file: File }) => {
      tooBig(file)
      const body = new FormData()
      body.append("file", file)
      await api(`/api/products/${product.id}/images/${imageId}`, { method: "PUT", body })
    },
    onSuccess: async () => { await refresh(); toast.success("Photo replaced") },
    onError: (e) => toast.error(errorMessage(e)),
    onSettled: () => setReplacing(null),
  })
  const primary = useMutation({
    mutationFn: (imageId: string) => api(`/api/products/${product.id}/images/${imageId}/primary`, { method: "PUT" }),
    onSuccess: async () => { await refresh(); toast.success("Main photo changed") },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const reorder = useMutation({
    mutationFn: (imageIds: string[]) => api(`/api/products/${product.id}/images/order`, { method: "PUT", body: { imageIds } }),
    onSuccess: refresh, onError: (e) => toast.error(errorMessage(e)),
  })
  const remove = useMutation({
    mutationFn: (imageId: string) => api(`/api/products/${product.id}/images/${imageId}`, { method: "DELETE" }),
    onSuccess: async () => { setConfirmDelete(null); await refresh(); toast.success("Photo deleted") },
    onError: (e) => { setConfirmDelete(null); toast.error(errorMessage(e)) },
  })
  const images = [...product.images].sort((a, b) => a.displayOrder - b.displayOrder)
  const move = (index: number, by: -1 | 1) => {
    const ids = images.map((i) => i.id)
    ;[ids[index], ids[index + by]] = [ids[index + by], ids[index]]
    reorder.mutate(ids)
  }
  const busy = upload.isPending || replace.isPending || primary.isPending || reorder.isPending || remove.isPending

  return (
    <Section title="Photos" actions={canEdit && (
      <>
        <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" multiple hidden
          onChange={(e) => {
            if (e.target.files?.length) {
              const files = Array.from(e.target.files)
              setPendingImages(files)
              upload.mutate(files)
            }
            e.target.value = ""
          }} />
        <input ref={replaceInput} type="file" accept="image/jpeg,image/png,image/webp" hidden
          onChange={(e) => {
            const file = e.target.files?.[0]
            const imageId = replaceTarget.current
            if (file && imageId) { setReplacing(imageId); replace.mutate({ imageId, file }) }
            e.target.value = ""
          }} />
        <Button size="sm" variant="outline" onClick={() => input.current?.click()} disabled={upload.isPending}>
          {upload.isPending ? <InlineSpinner /> : <ImagePlus />} {upload.isPending ? "Uploading…" : "Add photos"}
        </Button>
      </>
    )} bodyClassName="p-3">
      {images.length === 0 && pendingImages.length === 0 ? (
        <p className="px-1 py-6 text-center text-xs text-muted-foreground">No photos yet. JPEG, PNG or WebP up to 5 MB.</p>
      ) : (
        <ul className="grid grid-cols-2 gap-2">
          {images.map((img, index) => (
            <li key={img.id} className={cn("overflow-hidden rounded-md border bg-white", img.isPrimary ? "border-rose" : "border-hairline")}>
              <div className="relative">
                <img src={`/api/products/${product.id}/images/${img.id}`} alt={`${product.name} photo ${index + 1}`}
                  className={cn("aspect-square w-full object-cover", replacing === img.id && "opacity-40")} />
                {img.isPrimary && <span className="absolute top-1.5 left-1.5 rounded bg-white/90 px-1.5 py-0.5 text-[0.65rem] font-medium text-rose-ink">Main</span>}
                {replacing === img.id && <span className="absolute inset-0 grid place-items-center"><InlineSpinner className="size-5 text-rose-ink" /></span>}
              </div>
              {canEdit && (
                <div className="flex items-center justify-between gap-0.5 border-t border-hairline px-1 py-1">
                  <div className="flex">
                    <Button size="icon-xs" variant="ghost" title="Move earlier" aria-label="Move photo earlier" disabled={busy || index === 0} onClick={() => move(index, -1)}><ArrowLeft /></Button>
                    <Button size="icon-xs" variant="ghost" title="Move later" aria-label="Move photo later" disabled={busy || index === images.length - 1} onClick={() => move(index, 1)}><ArrowRight /></Button>
                  </div>
                  <div className="flex">
                    <Button size="icon-xs" variant="ghost" title={img.isPrimary ? "This is the main photo" : "Make main photo"} aria-label="Make main photo"
                      disabled={busy || img.isPrimary} onClick={() => primary.mutate(img.id)}>
                      <Star className={img.isPrimary ? "fill-rose text-rose" : undefined} />
                    </Button>
                    <Button size="icon-xs" variant="ghost" title="Replace photo" aria-label="Replace photo" disabled={busy}
                      onClick={() => { replaceTarget.current = img.id; replaceInput.current?.click() }}><RefreshCw /></Button>
                    <Button size="icon-xs" variant="ghost" title="Delete photo" aria-label="Delete photo" disabled={busy}
                      className="hover:text-destructive" onClick={() => setConfirmDelete(img.id)}><Trash2 /></Button>
                  </div>
                </div>
              )}
            </li>
          ))}
          {pendingImages.map((file) => <PendingImagePreview key={`${file.name}-${file.size}-${file.lastModified}`} file={file} />)}
        </ul>
      )}
      <ConfirmDialog open={!!confirmDelete} onOpenChange={(o) => !o && setConfirmDelete(null)} destructive
        title="Delete this photo?" confirmLabel="Delete photo" pending={remove.isPending}
        onConfirm={() => confirmDelete && remove.mutate(confirmDelete)}
        description={images.find((i) => i.id === confirmDelete)?.isPrimary && images.length > 1
          ? "This is the main photo; the next photo will become the main one."
          : "The photo is removed permanently."} />
    </Section>
  )
}

function PendingImagePreview({ file }: { file: File }) {
  const [src] = useState(() => URL.createObjectURL(file))
  useEffect(() => () => URL.revokeObjectURL(src), [src])
  return (
    <li className="relative overflow-hidden rounded-md border border-rose bg-white">
      <img src={src} alt="Photo being uploaded" className="aspect-square w-full object-cover" />
      <span className="absolute top-1.5 left-1.5 flex items-center gap-1 rounded bg-white/90 px-1.5 py-0.5 text-[0.65rem] font-medium text-rose-ink"><InlineSpinner className="size-2.5" />Uploading</span>
    </li>
  )
}
