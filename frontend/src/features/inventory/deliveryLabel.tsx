import { date } from "@/lib/format"

/** Staff know stock by when it arrived; the batch code only matters when matching expiry dates printed on packs. */
export function deliveryLabel(b: { batchNumber: string; receivedAt: string | null }, expiryEnabled: boolean) {
  const when = b.receivedAt ? `Added ${date(b.receivedAt)}` : "Earlier stock"
  return expiryEnabled ? <>{when} <span className="code text-xs text-muted-foreground">{b.batchNumber}</span></> : when
}
