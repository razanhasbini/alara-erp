import { Link, useSearchParams } from "react-router"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { Boxes, Eye, LogIn, Package, ReceiptText, Settings2, ShoppingBag, UserRound, Users, type LucideIcon } from "lucide-react"
import { Pager } from "@/components/common/DataTable"
import { SearchInput } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { EmptyState, ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { api } from "@/lib/api"
import { BUSINESS_TIME_ZONE, dateTime, todayIso } from "@/lib/format"
import { qk } from "@/lib/queries"
import type { ActivityArea, ActivityEntry, Paged, UserSummary } from "@/types/api"

const areas: Record<ActivityArea, { label: string; icon: LucideIcon }> = {
  Orders: { label: "Orders", icon: ShoppingBag },
  Customers: { label: "Customers", icon: UserRound },
  Stock: { label: "Stock", icon: Boxes },
  Products: { label: "Products", icon: Package },
  Bills: { label: "Bills", icon: ReceiptText },
  Employees: { label: "Employees", icon: Users },
  SignIn: { label: "Signing in", icon: LogIn },
  Settings: { label: "Settings", icon: Settings2 },
  Viewed: { label: "Pages opened", icon: Eye },
}

const dayKey = new Intl.DateTimeFormat("en-CA", { timeZone: BUSINESS_TIME_ZONE })
const dayTitle = new Intl.DateTimeFormat("en-GB", { timeZone: BUSINESS_TIME_ZONE, weekday: "long", day: "numeric", month: "long", year: "numeric" })
const clockTime = new Intl.DateTimeFormat("en-GB", { timeZone: BUSINESS_TIME_ZONE, hour: "2-digit", minute: "2-digit", hour12: false })

function dayHeading(key: string, sample: Date) {
  const today = todayIso()
  if (key === today) return "Today"
  if (key === dayKey.format(new Date(Date.now() - 86_400_000))) return "Yesterday"
  return dayTitle.format(sample)
}

/** Who did what and when, written so anyone can read it: "Maya entered order AL-000123 for Rima…". */
export function ActivityPage() {
  const [params, setParams] = useSearchParams()
  const userId = params.get("person") ?? undefined
  const area = (params.get("area") ?? undefined) as ActivityArea | undefined
  const day = params.get("day") ?? undefined
  const search = params.get("search") ?? ""
  const page = Number(params.get("page") ?? 1)
  const set = (key: string, value?: string) => setParams((p) => {
    const next = new URLSearchParams(p)
    if (value) next.set(key, value); else next.delete(key)
    if (key !== "page") next.delete("page")
    return next
  }, { replace: true })

  const query = { userId, area, from: day, to: day, search: search || undefined, page, pageSize: 50 }
  const { data, isLoading, isFetching, error, refetch } = useQuery({
    queryKey: qk.activity(query),
    queryFn: () => api<Paged<ActivityEntry>>("/api/activity", { query }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  })
  const { data: people } = useQuery({
    queryKey: qk.users({ pageSize: 100 }),
    queryFn: () => api<Paged<UserSummary>>("/api/users", { query: { pageSize: 100 } }),
  })

  if (isLoading) return <FullPageSpinner />
  if (error) return <ErrorState error={error} onRetry={refetch} />

  const groups: { key: string; title: string; items: ActivityEntry[] }[] = []
  for (const entry of data?.items ?? []) {
    const at = new Date(entry.at)
    const key = dayKey.format(at)
    const last = groups.at(-1)
    if (last?.key === key) last.items.push(entry)
    else groups.push({ key, title: dayHeading(key, at), items: [entry] })
  }
  const filtered = !!(userId || area || day || search)

  return (
    <>
      <PageHeader title="Activity log" description="Everything people do in the system, newest first: orders entered, stock received, customers added, pages checked."
        meta={isFetching && <span className="flex items-center gap-1.5 text-xs text-muted-foreground"><InlineSpinner className="size-3" /> Updating</span>} />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Select value={userId ?? "all"} onValueChange={(v) => set("person", v === "all" ? undefined : v)}>
          <SelectTrigger className="h-8 w-48 bg-card" aria-label="Person"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Everyone</SelectItem>
            {people?.items.map((u) => <SelectItem key={u.id} value={u.id}>{`${u.firstName} ${u.lastName}`.trim()}</SelectItem>)}
          </SelectContent>
        </Select>
        <Select value={area ?? "all"} onValueChange={(v) => set("area", v === "all" ? undefined : v)}>
          <SelectTrigger className="h-8 w-44 bg-card" aria-label="Kind of activity"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All activity</SelectItem>
            {Object.entries(areas).map(([k, v]) => <SelectItem key={k} value={k}>{v.label}</SelectItem>)}
          </SelectContent>
        </Select>
        <Input type="date" aria-label="Day" value={day ?? ""} max={todayIso()} className="h-8 w-40 bg-card"
          onChange={(e) => set("day", e.target.value || undefined)} />
        <SearchInput value={search} onChange={(v) => set("search", v || undefined)} placeholder="Search (name, order number…)" />
        {filtered && <Button size="sm" variant="ghost" onClick={() => setParams({}, { replace: true })}>Clear filters</Button>}
      </div>

      {groups.length ? (
        <div className="space-y-6">
          {groups.map((g) => (
            <section key={g.key} aria-labelledby={`day-${g.key}`}>
              <h2 id={`day-${g.key}`} className="eyebrow mb-2">{g.title}</h2>
              <ul className="overflow-hidden rounded-lg border border-hairline bg-card">
                {g.items.map((entry) => <ActivityRow key={entry.id} entry={entry} />)}
              </ul>
            </section>
          ))}
        </div>
      ) : (
        <div className="rounded-lg border border-hairline bg-card">
          <EmptyState title={filtered ? "Nothing matches" : "No activity yet"}>
            {filtered ? "Try another person, day or search." : "As people use the system, what they do will show up here."}
          </EmptyState>
        </div>
      )}
      {data && data.totalCount > data.pageSize && (
        <div className="mt-4 rounded-lg border border-hairline bg-card">
          <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={(p) => set("page", String(p))} />
        </div>
      )}
    </>
  )
}

function ActivityRow({ entry }: { entry: ActivityEntry }) {
  const { icon: Icon, label } = areas[entry.area] ?? areas.Settings
  const quiet = entry.area === "Viewed" || entry.area === "SignIn"
  return (
    <li className="flex items-start gap-3 border-b border-hairline px-4 py-3 last:border-0">
      <time dateTime={entry.at} title={dateTime(entry.at)} className="w-11 shrink-0 pt-1 text-xs text-muted-foreground tabular">
        {clockTime.format(new Date(entry.at))}
      </time>
      <span className="mt-0.5 hidden size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground sm:grid" title={label}>
        <Icon className="size-3.5" aria-hidden /><span className="sr-only">{label}</span>
      </span>
      <p className={quiet ? "min-w-0 flex-1 pt-0.5 text-sm text-muted-foreground" : "min-w-0 flex-1 pt-0.5 text-sm leading-5"}>
        <span className="font-semibold text-foreground">{entry.who}</span> {entry.summary}
      </p>
      {entry.link && <Button size="xs" variant="outline" asChild className="shrink-0"><Link to={entry.link}>Open</Link></Button>}
    </li>
  )
}
