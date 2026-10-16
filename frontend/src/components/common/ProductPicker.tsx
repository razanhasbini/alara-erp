import { useState } from "react"
import { useInfiniteQuery } from "@tanstack/react-query"
import { ChevronsUpDown, Package } from "lucide-react"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Button } from "@/components/ui/button"
import { ProductThumb } from "@/components/common/ProductThumb"
import { api } from "@/lib/api"
import { money } from "@/lib/format"
import { useAvailability } from "@/lib/queries"
import { cn } from "@/lib/utils"
import type { Paged, ProductListItem, ProductType } from "@/types/api"

/** Server-searched product combobox. Shows price, type and (optionally) sellable stock. */
export function ProductPicker({ onSelect, type, exclude = [], warehouseId, showAvailability, placeholder = "Add product…", className, id }: {
  onSelect: (product: ProductListItem) => void
  type?: ProductType
  exclude?: string[]
  warehouseId?: string
  showAvailability?: boolean
  placeholder?: string
  className?: string
  id?: string
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState("")
  const { data, isFetching, isFetchingNextPage, fetchNextPage, hasNextPage } = useInfiniteQuery({
    queryKey: ["catalog", "picker", search, type],
    queryFn: ({ pageParam }) => api<Paged<ProductListItem>>("/api/products", {
      query: { search, type, isActive: true, page: pageParam, pageSize: 100 },
    }),
    initialPageParam: 1,
    getNextPageParam: (page) => page.page < page.totalPages ? page.page + 1 : undefined,
    enabled: open,
    staleTime: 30_000,
  })
  const items = (data?.pages.flatMap((page) => page.items) ?? []).filter((p) => !exclude.includes(p.id))
  const { data: available } = useAvailability(items.map((i) => i.id), warehouseId, open && !!showAvailability)

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button id={id} type="button" variant="outline" role="combobox" aria-expanded={open}
          className={cn("w-full justify-between bg-card font-normal text-muted-foreground", className)}>
          <span className="flex items-center gap-2"><Package className="size-3.5" /> {placeholder}</span>
          <ChevronsUpDown className="size-3.5 opacity-60" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-[min(30rem,calc(100vw-2rem))] p-0" align="start">
        <Command shouldFilter={false}>
          <CommandInput placeholder="Search by name" value={search} onValueChange={setSearch} />
          <CommandList onScroll={(event) => {
            const list = event.currentTarget
            if (hasNextPage && !isFetchingNextPage && list.scrollHeight - list.scrollTop - list.clientHeight < 48)
              void fetchNextPage()
          }}>
            <CommandEmpty>{isFetching ? "Searching…" : `No matching active ${type === "Bundle" ? "bundles" : "products"}.`}</CommandEmpty>
            <CommandGroup>
              {items.map((p) => {
                const qty = available?.[p.id]
                return (
                  <CommandItem key={p.id} value={p.id} onSelect={() => { onSelect(p); setOpen(false); setSearch("") }}
                    className="flex items-center justify-between gap-3">
                    <span className="flex min-w-0 items-center gap-3">
                      <ProductThumb product={p} size="size-10" />
                      <span className="min-w-0">
                        <span className="block truncate">{p.name}</span>
                      </span>
                    </span>
                    <span className="shrink-0 text-right text-xs">
                      <span className="block tabular">{money(p.sellingPrice)}</span>
                      {showAvailability && qty !== undefined && (
                        <span className={cn("block tabular", qty <= 0 ? "text-tone-red" : "text-muted-foreground")}>{qty} avail.</span>
                      )}
                    </span>
                  </CommandItem>
                )
              })}
            </CommandGroup>
            {hasNextPage && (
              <button type="button" onClick={() => void fetchNextPage()} disabled={isFetchingNextPage}
                className="w-full border-t px-3 py-2.5 text-center text-sm font-medium text-primary hover:bg-muted disabled:opacity-60">
                {isFetchingNextPage ? "Loading more products…" : "Load more products"}
              </button>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  )
}
