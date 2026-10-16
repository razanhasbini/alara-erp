import { cn } from "@/lib/utils"

export function ProductThumb({ product, size = "size-9" }: {
  product: { id: string; primaryImageId: string | null; name: string }
  size?: string
}) {
  return product.primaryImageId ? (
    <img src={`/api/products/${product.id}/images/${product.primaryImageId}`} alt="" loading="lazy"
      className={cn(size, "shrink-0 rounded-md border border-hairline bg-white object-cover")} />
  ) : (
    <span aria-hidden className={cn(size, "font-display grid shrink-0 place-items-center rounded-md bg-blush text-sm text-rose-ink")}>
      {product.name.replace(/[^A-Za-zÀ-ÿ]/g, "").charAt(0)}
    </span>
  )
}
