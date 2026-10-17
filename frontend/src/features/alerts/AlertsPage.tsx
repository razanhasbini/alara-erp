import { useState } from "react"
import { Link } from "react-router"
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { AlarmClock, Bell, CheckCheck, MessageCircle, PackageMinus, RefreshCw } from "lucide-react"
import { toast } from "sonner"
import { Pager } from "@/components/common/DataTable"
import { Segmented } from "@/components/common/controls"
import { PageHeader } from "@/components/common/page"
import { Pill } from "@/components/common/StatusBadge"
import { EmptyState, ErrorState, FullPageSpinner, InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { api, errorMessage } from "@/lib/api"
import { dateTime, when } from "@/lib/format"
import { alertKeys } from "@/lib/alerts"
import { cn } from "@/lib/utils"
import type { AlertChannels, AlertItem, Paged, WhatsAppStatus } from "@/types/api"

const whatsAppCopy: Record<WhatsAppStatus, { label: string; tone: "green" | "amber" | "red" | "neutral" }> = {
  Sent: { label: "Sent on WhatsApp", tone: "green" },
  Pending: { label: "Sending on WhatsApp", tone: "amber" },
  Failed: { label: "WhatsApp failed", tone: "red" },
  NotConfigured: { label: "WhatsApp not connected", tone: "neutral" },
}

export function AlertsPage() {
  const queryClient = useQueryClient()
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [page, setPage] = useState(1)
  const { data: channels } = useQuery({ queryKey: alertKeys.channels, queryFn: () => api<AlertChannels>("/api/notifications/channels") })
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: alertKeys.list(unreadOnly, page),
    queryFn: () => api<Paged<AlertItem>>("/api/notifications", { query: { unreadOnly, page, pageSize: 25 } }),
    placeholderData: keepPreviousData,
    refetchInterval: 60_000,
  })
  const refresh = () => queryClient.invalidateQueries({ queryKey: alertKeys.all })

  const markRead = useMutation({
    mutationFn: (id: string) => api<void>(`/api/notifications/${id}/read`, { method: "POST" }),
    onSuccess: refresh,
  })
  const markAll = useMutation({
    mutationFn: () => api<{ count: number }>("/api/notifications/read-all", { method: "POST" }),
    onSuccess: async (result) => { await refresh(); toast.success(result.count ? `${result.count} alert${result.count === 1 ? "" : "s"} marked as read` : "Nothing new") },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const resend = useMutation({
    mutationFn: (id: string) => api<void>(`/api/notifications/${id}/resend-whatsapp`, { method: "POST" }),
    onSuccess: async () => { await refresh(); toast.success("Queued — it will be sent on WhatsApp within a minute") },
    onError: (e) => toast.error(errorMessage(e)),
  })

  if (isLoading) return <FullPageSpinner />
  if (error) return <ErrorState error={error} onRetry={refetch} />

  return (
    <>
      <PageHeader title="Alerts" description="Everything the system sends on WhatsApp is kept here too, so nothing is missed if a message isn't seen."
        actions={<Button variant="outline" disabled={markAll.isPending} onClick={() => markAll.mutate()}>{markAll.isPending ? <InlineSpinner /> : <CheckCheck />} Mark all as read</Button>} />

      {channels && !channels.whatsAppConfigured && (
        <div role="status" className="mb-4 flex items-start gap-3 rounded-lg border border-hairline bg-card px-4 py-3 text-sm">
          <MessageCircle className="mt-0.5 size-5 shrink-0 text-muted-foreground" aria-hidden />
          <div>
            <p className="font-medium">WhatsApp isn't connected yet</p>
            <p className="mt-0.5 leading-5 text-muted-foreground">Alerts are recorded here only. Once the WhatsApp Business number is connected, new alerts are also sent on WhatsApp, and older ones can be re-sent from this page.</p>
          </div>
        </div>
      )}
      {channels?.whatsAppConfigured && channels.whatsAppNotSent > 0 && (
        <div role="status" className="mb-4 rounded-lg border border-tone-amber/30 bg-tone-amber-bg px-4 py-3 text-sm text-tone-amber">
          {channels.whatsAppNotSent} WhatsApp message{channels.whatsAppNotSent === 1 ? " was" : "s were"} not delivered. Use “Send on WhatsApp” on an alert to try again.
        </div>
      )}

      <div className="mb-3 flex items-center gap-2">
        <Segmented label="Show" value={unreadOnly ? "unread" : "all"} onChange={(v) => { setUnreadOnly(v === "unread"); setPage(1) }}
          options={[{ value: "all", label: "All" }, { value: "unread", label: "Unread" }]} />
      </div>

      <div className="overflow-hidden rounded-lg border border-hairline bg-card">
        {data?.items.length ? (
          <ul>
            {data.items.map((alert) => (
              <AlertRow key={alert.id} alert={alert} canResend={!!channels?.whatsAppConfigured}
                resending={resend.isPending && resend.variables === alert.id}
                onRead={() => !alert.isRead && markRead.mutate(alert.id)} onResend={() => resend.mutate(alert.id)} />
            ))}
          </ul>
        ) : (
          <EmptyState title={unreadOnly ? "You're all caught up" : "No alerts yet"}>
            {unreadOnly ? "No unread alerts." : "When a product runs low on stock you'll see it here (and on WhatsApp once it's connected)."}
          </EmptyState>
        )}
        {data && data.totalCount > data.pageSize && <Pager page={data.page} pageSize={data.pageSize} total={data.totalCount} onPage={setPage} />}
      </div>
    </>
  )
}

function AlertRow({ alert, canResend, resending, onRead, onResend }: {
  alert: AlertItem; canResend: boolean; resending: boolean; onRead: () => void; onResend: () => void
}) {
  const Icon = alert.type === "stock.low" ? PackageMinus : alert.type === "reservation.expired" ? AlarmClock : Bell
  const undelivered = alert.whatsApp.some((m) => m.status === "Failed" || m.status === "NotConfigured")
  const statuses = [...new Set(alert.whatsApp.map((m) => m.status))]
  return (
    <li className={cn("flex gap-3 border-b border-hairline px-4 py-3 last:border-0", !alert.isRead && "bg-blush/40")}>
      <span className={cn("mt-0.5 grid size-8 shrink-0 place-items-center rounded-full", alert.isRead ? "bg-secondary text-muted-foreground" : "bg-rose text-white")}>
        <Icon className="size-4" aria-hidden />
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline gap-x-2">
          <p className={cn("text-sm", !alert.isRead && "font-semibold")}>{alert.title}</p>
          {!alert.isRead && <span className="sr-only">(unread)</span>}
          <span className="text-xs text-muted-foreground" title={dateTime(alert.createdAt)}>{when(alert.createdAt)}</span>
        </div>
        <p className="mt-1 text-sm leading-5 whitespace-pre-line text-muted-foreground">{alert.body}</p>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          {statuses.map((status) => (
            <Pill key={status} tone={whatsAppCopy[status].tone}>{whatsAppCopy[status].label}</Pill>
          ))}
          {alert.whatsApp.filter((m) => m.status === "Failed" && m.lastError).slice(0, 1).map((m) => (
            <span key={m.id} className="text-xs text-tone-red">{m.lastError}</span>
          ))}
          <div className="ml-auto flex items-center gap-1">
            {canResend && undelivered && (
              <Button size="xs" variant="ghost" disabled={resending} onClick={onResend}>{resending ? <InlineSpinner /> : <RefreshCw />} Send on WhatsApp</Button>
            )}
            {!alert.isRead && <Button size="xs" variant="ghost" onClick={onRead}>Mark as read</Button>}
            {alert.link && <Button size="xs" variant="outline" asChild><Link to={alert.link} onClick={onRead}>Open</Link></Button>}
          </div>
        </div>
      </div>
    </li>
  )
}
