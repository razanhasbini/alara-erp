import { cn } from "@/lib/utils"
import { humanize } from "@/lib/format"
import type { OrderStatus, PaymentStatus, StockStatus } from "@/types/api"

type Tone = "neutral" | "blue" | "amber" | "green" | "red" | "rose"

const toneClass: Record<Tone, string> = {
  neutral: "bg-tone-neutral-bg text-tone-neutral",
  blue: "bg-tone-blue-bg text-tone-blue",
  amber: "bg-tone-amber-bg text-tone-amber",
  green: "bg-tone-green-bg text-tone-green",
  red: "bg-tone-red-bg text-tone-red",
  rose: "bg-blush text-rose-ink",
}

export function Pill({ tone = "neutral", children, className, dot = true }: { tone?: Tone; children: React.ReactNode; className?: string; dot?: boolean }) {
  return (
    <span className={cn("inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[0.72rem] font-medium whitespace-nowrap", toneClass[tone], className)}>
      {dot && <span className="size-1.5 rounded-full bg-current opacity-70" aria-hidden />}
      {children}
    </span>
  )
}

const orderTone: Record<OrderStatus, Tone> = {
  Draft: "neutral", Reserved: "rose", Confirmed: "blue", Shipped: "amber", Delivered: "green", Cancelled: "neutral", Returned: "red",
}
const orderLabel: Partial<Record<OrderStatus, string>> = { Confirmed: "Sold" }
export const OrderStatusPill = ({ status }: { status: OrderStatus }) => (
  <Pill tone={orderTone[status]} className={status === "Cancelled" ? "line-through decoration-1" : undefined}>{orderLabel[status] ?? status}</Pill>
)

const paymentTone: Record<PaymentStatus, Tone> = { Unpaid: "neutral", CollectedByCourier: "amber", Paid: "green", Void: "neutral" }
export const PaymentStatusPill = ({ status }: { status: PaymentStatus }) => (
  <Pill tone={paymentTone[status]} dot={false}>{humanize(status)}</Pill>
)

const stockTone: Record<StockStatus, Tone> = { InStock: "green", Low: "amber", Out: "red" }
const stockLabel: Record<StockStatus, string> = { InStock: "In stock", Low: "Low", Out: "Out of stock" }
export const StockStatusPill = ({ status }: { status: StockStatus }) => <Pill tone={stockTone[status]}>{stockLabel[status]}</Pill>

export const ActivePill = ({ active }: { active: boolean }) =>
  active ? <Pill tone="green" dot={false}>Active</Pill> : <Pill tone="neutral" dot={false}>Inactive</Pill>
