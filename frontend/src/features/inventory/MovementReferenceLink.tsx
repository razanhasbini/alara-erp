import { Link } from "react-router"
import type { Movement } from "@/types/api"

/** Links a ledger entry to the document that caused it (order, receipt, transfer). */
export function MovementReferenceLink({ movement: m }: { movement: Pick<Movement, "referenceType" | "referenceId" | "referenceNumber"> }) {
  const to = m.referenceType === "Order" ? `/orders/${m.referenceId}`
    : m.referenceType === "Receipt" ? `/inventory/receipts/${m.referenceId}`
    : m.referenceType === "Transfer" ? `/inventory/transfers/${m.referenceId}` : null
  const label = m.referenceType === "Adjustment" ? "Manual" : m.referenceNumber ?? m.referenceType
  return to
    ? <Link to={to} className="code hover:underline" onClick={(e) => e.stopPropagation()}>{label}</Link>
    : <span className="text-muted-foreground">{label}</span>
}
