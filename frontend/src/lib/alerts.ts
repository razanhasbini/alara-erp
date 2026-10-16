import { useQuery } from "@tanstack/react-query"
import { api } from "@/lib/api"

export const alertKeys = {
  all: ["alerts"] as const,
  unread: ["alerts", "unread-count"] as const,
  channels: ["alerts", "channels"] as const,
  list: (unreadOnly: boolean, page: number) => ["alerts", "list", unreadOnly, page] as const,
}

/** Polled by the top bar so a new alert shows up without a reload. */
export function useUnreadAlerts() {
  return useQuery({
    queryKey: alertKeys.unread,
    queryFn: () => api<{ count: number }>("/api/notifications/unread-count").then((r) => r.count),
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
  })
}
