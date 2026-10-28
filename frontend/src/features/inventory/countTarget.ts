import { date } from "@/lib/format"
import type { ProductStock } from "@/types/api"

export interface CountTarget {
  productId: string
  productName: string
  /** Where the product can be counted, with the units recorded there now. */
  warehouses: { warehouseId: string; code: string; onHand: number }[]
  /** Correct one delivery instead of the whole product. label: e.g. "the delivery added 16 Oct 2026". */
  batch?: { id: string; label: string }
  /** No delivery yet and the product tracks expiry dates: new units need one. */
  needsExpiry: boolean
}

/** The whole product, or one delivery (batch) of it, as shown on the product and inventory pages. */
export function countTarget(stock: ProductStock, expiryEnabled: boolean, batch?: ProductStock["batches"][number]): CountTarget {
  return {
    productId: stock.productId,
    productName: stock.name,
    warehouses: batch
      ? [{ warehouseId: batch.warehouseId, code: batch.warehouseCode, onHand: batch.onHand }]
      : stock.warehouses.map((w) => ({ warehouseId: w.warehouseId, code: w.code, onHand: w.onHand })),
    batch: batch && { id: batch.batchId, label: expiryEnabled ? `batch ${batch.batchNumber}` : batch.receivedAt ? `the delivery added ${date(batch.receivedAt)}` : "earlier stock" },
    needsExpiry: expiryEnabled && stock.settings.tracksExpiry && stock.batches.length === 0,
  }
}
