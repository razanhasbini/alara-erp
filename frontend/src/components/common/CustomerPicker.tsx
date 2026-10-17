import { useState } from "react"
import { useInfiniteQuery } from "@tanstack/react-query"
import { Check, ChevronsUpDown, UserRound, X } from "lucide-react"
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Button } from "@/components/ui/button"
import { api } from "@/lib/api"
import { cn } from "@/lib/utils"
import type { Customer, Paged } from "@/types/api"
import { formatPhone } from "@/lib/phone"

/** Server-searched customer selector used during order entry. */
export function CustomerPicker({ selectedId, selectedLabel, onSelect, onClear, className }: {
  selectedId?: string | null
  selectedLabel?: string
  onSelect: (customer: Customer) => void
  onClear?: () => void
  className?: string
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState("")
  const { data, isFetching, isFetchingNextPage, fetchNextPage, hasNextPage } = useInfiniteQuery({
    queryKey: ["sales", "customer-picker", search],
    queryFn: ({ pageParam }) => api<Paged<Customer>>("/api/customers", { query: { search, isActive: true, page: pageParam, pageSize: 100 } }),
    initialPageParam: 1,
    getNextPageParam: (page) => page.page < page.totalPages ? page.page + 1 : undefined,
    enabled: open,
    staleTime: 30_000,
  })
  const customers = data?.pages.flatMap((page) => page.items) ?? []

  return (
    <div className={cn("flex gap-2", className)}>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <Button type="button" variant="outline" role="combobox" aria-expanded={open}
            className={cn("min-w-0 flex-1 justify-between bg-card font-normal", !selectedId && "text-muted-foreground")}>
            <span className="flex min-w-0 items-center gap-2"><UserRound className="size-4 shrink-0" /><span className="truncate">{selectedId ? selectedLabel : "Find an existing customer…"}</span></span>
            <ChevronsUpDown className="size-4 shrink-0 opacity-60" />
          </Button>
        </PopoverTrigger>
        <PopoverContent className="w-[min(34rem,calc(100vw-2rem))] p-0" align="start">
          <Command shouldFilter={false}>
            <CommandInput placeholder="Search name, phone, email or city" value={search} onValueChange={setSearch} />
            <CommandList onScroll={(event) => {
              const list = event.currentTarget
              if (hasNextPage && !isFetchingNextPage && list.scrollHeight - list.scrollTop - list.clientHeight < 48) void fetchNextPage()
            }}>
              <CommandEmpty>{isFetching ? "Searching…" : "No matching active customer. Enter their details below to create one with the order."}</CommandEmpty>
              <CommandGroup>
                {customers.map((customer) => (
                  <CommandItem key={customer.id} value={customer.id} onSelect={() => { onSelect(customer); setOpen(false); setSearch("") }}
                    className="flex items-center gap-3">
                    <Check className={cn("size-4 shrink-0", customer.id === selectedId ? "opacity-100" : "opacity-0")} />
                    <span className="min-w-0 flex-1"><span className="block truncate font-medium">{customer.name}</span>
                      <span className="block truncate text-xs text-muted-foreground">{formatPhone(customer.phone)}{customer.city && ` · ${customer.city}`}</span></span>
                    <span className="shrink-0 text-xs tabular text-muted-foreground">{customer.orderCount} order{customer.orderCount === 1 ? "" : "s"}</span>
                  </CommandItem>
                ))}
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>
      {selectedId && onClear && <Button type="button" variant="ghost" size="icon" aria-label="Clear selected customer" onClick={onClear}><X /></Button>}
    </div>
  )
}
