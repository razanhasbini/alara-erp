import { Minus, Plus, X } from "lucide-react"
import { ProductPicker } from "@/components/common/ProductPicker"
import { Button } from "@/components/ui/button"
import { money } from "@/lib/format"

export interface ComponentLine { productId: string; name: string; sku: string; unitPrice: number; quantity: number }

/** Edits a bundle's composition; shows the full-price value the bundle is discounted against. */
export function BundleComponentsEditor({ value, onChange, bundlePrice }: {
  value: ComponentLine[]; onChange: (lines: ComponentLine[]) => void; bundlePrice?: number
}) {
  const total = value.reduce((s, l) => s + l.unitPrice * l.quantity, 0)
  const update = (i: number, quantity: number) => onChange(value.map((l, j) => (j === i ? { ...l, quantity } : l)))
  return (
    <div className="grid gap-2">
      {value.length > 0 && (
        <ul className="divide-y divide-hairline rounded-md border border-hairline bg-card">
          {value.map((l, i) => (
            <li key={l.productId} className="flex items-center gap-3 px-3 py-2">
              <div className="min-w-0 flex-1">
                <div className="truncate text-sm">{l.name}</div>
                <div className="code text-muted-foreground">{l.sku} · {money(l.unitPrice)}</div>
              </div>
              <div className="flex items-center gap-1">
                <Button type="button" variant="ghost" size="icon-xs" aria-label={`Fewer ${l.name}`} disabled={l.quantity <= 1} onClick={() => update(i, l.quantity - 1)}><Minus /></Button>
                <span className="w-6 text-center text-sm tabular" aria-label="Quantity">{l.quantity}</span>
                <Button type="button" variant="ghost" size="icon-xs" aria-label={`More ${l.name}`} onClick={() => update(i, l.quantity + 1)}><Plus /></Button>
              </div>
              <Button type="button" variant="ghost" size="icon-xs" aria-label={`Remove ${l.name}`} onClick={() => onChange(value.filter((_, j) => j !== i))}><X /></Button>
            </li>
          ))}
        </ul>
      )}
      <ProductPicker type="Individual" exclude={value.map((l) => l.productId)} placeholder="Add a product to the bundle…"
        onSelect={(p) => onChange([...value, { productId: p.id, name: p.name, sku: p.sku, unitPrice: p.sellingPrice, quantity: 1 }])} />
      {value.length > 0 && (
        <p className="text-xs text-muted-foreground">
          Bought separately: <span className="tabular font-medium text-foreground">{money(total)}</span>
          {bundlePrice !== undefined && bundlePrice > 0 && bundlePrice < total && (
            <> · customer saves <span className="tabular font-medium text-rose-ink">{money(total - bundlePrice)}</span> ({Math.round((1 - bundlePrice / total) * 100)}%)</>
          )}
        </p>
      )}
    </div>
  )
}
