/** Formatting in the business's conventions: USD amounts, Beirut local time. */

export const BUSINESS_TIME_ZONE = "Asia/Beirut"

const moneyFormatters = new Map<string, Intl.NumberFormat>()

export function money(value: number | null | undefined, currency = "USD"): string {
  if (value === null || value === undefined) return "—"
  let f = moneyFormatters.get(currency)
  if (!f) {
    f = new Intl.NumberFormat("en-US", { style: "currency", currency, minimumFractionDigits: 2, maximumFractionDigits: 2 })
    moneyFormatters.set(currency, f)
  }
  return f.format(value)
}

const numberFormatter = new Intl.NumberFormat("en-US")
export const number = (value: number | null | undefined) => (value === null || value === undefined ? "—" : numberFormatter.format(value))

const dateFormatter = new Intl.DateTimeFormat("en-GB", { timeZone: BUSINESS_TIME_ZONE, day: "2-digit", month: "short", year: "numeric" })
const dateTimeFormatter = new Intl.DateTimeFormat("en-GB", {
  timeZone: BUSINESS_TIME_ZONE, day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", hour12: false,
})
const timeFormatter = new Intl.DateTimeFormat("en-GB", { timeZone: BUSINESS_TIME_ZONE, hour: "2-digit", minute: "2-digit", hour12: false })

export const date = (iso: string | null | undefined) => (iso ? dateFormatter.format(new Date(iso)) : "—")
export const dateTime = (iso: string | null | undefined) => (iso ? dateTimeFormatter.format(new Date(iso)) : "—")

/** "Today 14:05", "Yesterday 09:12", otherwise a full date-time. */
export function when(iso: string | null | undefined): string {
  if (!iso) return "—"
  const d = new Date(iso)
  const day = (x: Date) => dateFormatter.format(x)
  const now = new Date()
  const yesterday = new Date(now.getTime() - 86_400_000)
  if (day(d) === day(now)) return `Today ${timeFormatter.format(d)}`
  if (day(d) === day(yesterday)) return `Yesterday ${timeFormatter.format(d)}`
  return dateTimeFormatter.format(d)
}

/** A DateOnly ("2027-03-01") shown without time-zone shifting. */
export function dateOnly(value: string | null | undefined): string {
  if (!value) return "—"
  const [y, m, d] = value.split("-").map(Number)
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric", timeZone: "UTC" }).format(Date.UTC(y, m - 1, d))
}

/** Today's date in Beirut as yyyy-MM-dd (for date inputs). */
export function todayIso(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: BUSINESS_TIME_ZONE }).format(new Date())
}

export function daysUntil(value: string): number {
  const [y, m, d] = value.split("-").map(Number)
  const [ty, tm, td] = todayIso().split("-").map(Number)
  return Math.round((Date.UTC(y, m - 1, d) - Date.UTC(ty, tm - 1, td)) / 86_400_000)
}

/** "CashOnDelivery" → "Cash on delivery", "WhatsApp" stays. */
export function humanize(value: string): string {
  const special: Record<string, string> = { WhatsApp: "WhatsApp", Omt: "OMT", CollectedByCourier: "With courier" }
  if (special[value]) return special[value]
  const spaced = value.replace(/([a-z])([A-Z])/g, "$1 $2")
  return spaced.charAt(0) + spaced.slice(1).toLowerCase()
}

export function fullName(first?: string | null, last?: string | null) {
  return [first, last].filter(Boolean).join(" ")
}

export function percent(value: number | null | undefined, digits = 1): string {
  return value === null || value === undefined ? "—" : `${value.toFixed(digits)}%`
}

/** Unit costs can carry 4 decimals; show cents unless sub-cent precision matters. */
export function cost(value: number | null | undefined): string {
  if (value === null || value === undefined) return "—"
  const cents = Math.round(value * 100) / 100
  return Math.abs(cents - value) > 0.00001
    ? new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", minimumFractionDigits: 2, maximumFractionDigits: 4 }).format(value)
    : money(value)
}

/** Add days to a yyyy-MM-dd date. */
export function addDays(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split("-").map(Number)
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10)
}
