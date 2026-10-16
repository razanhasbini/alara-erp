import { addDays, todayIso } from "@/lib/format"
import type { Grouping } from "@/types/api"

export type Preset = "today" | "yesterday" | "this-week" | "last-week" | "this-month" | "last-month" | "last-30" | "last-90" | "this-year" | "custom"

export const PRESETS: { value: Preset; label: string }[] = [
  { value: "today", label: "Today" }, { value: "yesterday", label: "Yesterday" },
  { value: "this-week", label: "This week" }, { value: "last-week", label: "Last week" },
  { value: "this-month", label: "This month" }, { value: "last-month", label: "Last month" },
  { value: "last-30", label: "Last 30 days" }, { value: "last-90", label: "Last 90 days" },
  { value: "this-year", label: "This year" }, { value: "custom", label: "Custom range" },
]

const mondayOf = (iso: string) => {
  const [y, m, d] = iso.split("-").map(Number)
  const dow = (new Date(Date.UTC(y, m - 1, d)).getUTCDay() + 6) % 7
  return addDays(iso, -dow)
}

/** Date range (inclusive, Beirut calendar) and a sensible grouping for each preset. */
export function presetRange(preset: Preset): { from: string; to: string; groupBy: Grouping } {
  const today = todayIso()
  const [y, m] = today.split("-").map(Number)
  const firstOfMonth = `${y}-${String(m).padStart(2, "0")}-01`
  switch (preset) {
    case "today": return { from: today, to: today, groupBy: "Day" }
    case "yesterday": { const d = addDays(today, -1); return { from: d, to: d, groupBy: "Day" } }
    case "this-week": return { from: mondayOf(today), to: today, groupBy: "Day" }
    case "last-week": { const mon = addDays(mondayOf(today), -7); return { from: mon, to: addDays(mon, 6), groupBy: "Day" } }
    case "this-month": return { from: firstOfMonth, to: today, groupBy: "Day" }
    case "last-month": { const end = addDays(firstOfMonth, -1); return { from: `${end.slice(0, 7)}-01`, to: end, groupBy: "Day" } }
    case "last-90": return { from: addDays(today, -89), to: today, groupBy: "Week" }
    case "this-year": return { from: `${y}-01-01`, to: today, groupBy: "Month" }
    case "last-30":
    case "custom":
    default: return { from: addDays(today, -29), to: today, groupBy: "Day" }
  }
}
