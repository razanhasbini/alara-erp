import { useEffect } from "react"
import { api } from "@/lib/api"
import type { ViewedPage } from "@/types/api"

/** The server keeps one "checked the inventory" per person per half hour; this just avoids needless calls. */
const recentlySent = new Map<ViewedPage, number>()
const RESEND_AFTER_MS = 10 * 60_000

/** Notes in the activity log that the signed-in person opened this page. Never gets in the way if it fails. */
export function useLogView(page: ViewedPage) {
  useEffect(() => {
    const last = recentlySent.get(page)
    if (last && Date.now() - last < RESEND_AFTER_MS) return
    recentlySent.set(page, Date.now())
    api<void>("/api/activity/viewed", { method: "POST", body: { page } }).catch(() => recentlySent.delete(page))
  }, [page])
}
