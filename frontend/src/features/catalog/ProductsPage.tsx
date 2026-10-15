import { useCallback } from "react"
import { Link, useNavigate, useSearchParams } from "react-router"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { Plus } from "lucide-react"
import { useAuth } from "@/auth/auth"
import { DataTable, Pager, type Column } from "@/components/common/DataTable"
import { SearchInput, Segmented } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { ActivePill, Pill } from "@/components/common/StatusBadge"
import { EmptyState } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { api } from "@/lib/api"
import { money } from "@/lib/format"
import { P } from "@/lib/permissions"
import { qk, useAvailability, useCategories } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Paged, ProductListItem, ProductType } from "@/types/api"

export function ProductThumb({ product, size = "size-9" }: { product: { id: string; primaryImageId: string | null; name: string }; size?: string }) {
  return product.primaryImageId ? (
    <img src={`/api/products/${product.id}/images/${product.primaryImageId}`} alt="" loading="lazy"
      className={cn(size, "shrink-0 rounded-md border border-hairline bg-white object-cover")} />
  ) : (
    <span aria-hidden className={cn(size, "font-display grid shrink-0 place-items-center rounded-md bg-blush text-sm text-rose-ink")}>
      {product.name.replace(/[^A-Za-zÀ-ÿ]/g, "").charAt(0)}
    </span>
  )
}

export function ProductsPage({ type }: { type?: ProductType }) {
  const { can } = useAuth()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const search = params.get("search") ?? ""
  const categoryId = params.get("category") ?? ""
  const status = (params.get("status") ?? "active") as "active" | "inactive" | "all"
  const page = Number(params.get("page") ?? 1)

  const set = useCallback((patch: Record<string, string>) => {
    setParams((prev) => {
      const next = new URLSearchParams(prev)
      for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k) }
      if (!("page" in patch)) next.delete("page")
      return next
    }, { replace: true })
  }, [setParams])

  const query = { search, categoryId: categoryId || undefined, type, isActive: status === "all" ? undefined : status === "active", page, pageSize: 25 }
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: qk.products(query),
    queryFn: () => api<Paged<ProductListItem>>("/api/products", { query }),
    placeholderData: keepPreviousData,
  })
  const { data: categories } = useCategories()
  const { data: available } = useAvailability(data?.items.map((p) => p.id) ?? [], undefined, can(P.inventory.view))

  const columns: Column<ProductListItem>[] = [
    {
      key: "name", header: "Product",
      cell: (p) => (
        <div className="flex items-center gap-3">
          <ProductThumb product={p} />
          <div className="min-w-0">
            <div className="truncate font-medium">{p.name}</div>
            <div className="code text-muted-foreground">{p.sku}</div>
          </div>
        </div>
      ),
    },
    { key: "category", header: "Category", hideOnMobile: true, cell: (p) => p.categoryName ?? <span className="text-muted-foreground">—</span> },
    ...(!type ? [{ key: "type", header: "Type", hideOnMobile: true, cell: (p: ProductListItem) =>
      p.type === "Bundle" ? <Pill tone="rose" dot={false}>Bundle · {p.componentCount}</Pill> : <span className="text-muted-foreground">Single</span> }] : []),
    {
      key: "price", header: "Price", align: "right",
      cell: (p) => (
        <div className="tabular">
          <div className="font-medium">{money(p.sellingPrice)}</div>
          {p.compareAtPrice && p.compareAtPrice > p.sellingPrice && <div className="text-xs text-muted-foreground line-through">{money(p.compareAtPrice)}</div>}
        </div>
      ),
    },
    ...(can(P.inventory.view) ? [{
      key: "available", header: "Available", align: "right" as const,
      cell: (p: ProductListItem) => {
        const qty = available?.[p.id]
        return qty === undefined ? "…" : <span className={cn("tabular", qty <= 0 && "text-tone-red")}>{qty}</span>
      },
    }] : []),
    { key: "status", header: "Status", hideOnMobile: true, cell: (p) => <ActivePill active={p.isActive} /> },
  ]

  const isBundles = type === "Bundle"
  return (
    <>
      <PageHeader
        title={isBundles ? "Bundles" : "Products"}
        description={isBundles
          ? "Sets sold at their own price. Their stock is the stock of the products inside them."
          : "Everything ÀLara Line sells. Each scent is its own product."}
        actions={can(P.products.create) && (
          <Button asChild><Link to={isBundles ? "/products/new?type=Bundle" : "/products/new"}><Plus /> {isBundles ? "New bundle" : "New product"}</Link></Button>
        )}
      />
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <SearchInput value={search} onChange={(v) => set({ search: v })} placeholder="Search name, SKU, barcode" />
        <Select value={categoryId || "all"} onValueChange={(v) => set({ category: v === "all" ? "" : v })}>
          <SelectTrigger className="h-8 w-48 bg-card" aria-label="Category"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All categories</SelectItem>
            {categories?.map((c) => (
              <SelectItem key={c.id} value={c.id}>{c.parentName ? `${c.parentName} › ${c.name}` : c.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Segmented label="Status" value={status} onChange={(v) => set({ status: v === "active" ? "" : v })}
          options={[{ value: "active", label: "Active" }, { value: "inactive", label: "Inactive" }, { value: "all", label: "All" }]} />
      </div>
      <DataTable
        columns={columns} rows={data?.items} rowKey={(p) => p.id} loading={isLoading} error={error} onRetry={refetch}
        onRowClick={(p) => navigate(`/products/${p.id}`)}
        rowClassName={(p) => (p.isActive ? undefined : "opacity-60")}
        empty={<EmptyState title={search ? "No matches" : isBundles ? "No bundles yet" : "No products yet"}>
          {search ? "Try another name or SKU." : "Products you create appear here."}</EmptyState>}
        footer={data && data.totalCount > 0 && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(p) => set({ page: String(p) })} />}
      />
    </>
  )
}
