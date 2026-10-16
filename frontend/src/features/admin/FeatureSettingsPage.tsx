import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { CalendarClock } from "lucide-react"
import { toast } from "sonner"
import { PageHeader, Section } from "@/components/common/page"
import { ErrorState, FullPageSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { api, errorMessage } from "@/lib/api"
import { qk } from "@/lib/queries"
import type { FeatureSettings } from "@/types/api"

export function FeatureSettingsPage() {
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: qk.features,
    queryFn: () => api<FeatureSettings>("/api/settings/features"),
  })
  const update = useMutation({
    mutationFn: (enabled: boolean) => api<FeatureSettings>("/api/settings/features/expiry-dates", {
      method: "PUT", body: { enabled },
    }),
    onSuccess: async (value) => {
      queryClient.setQueryData(qk.features, value)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: qk.settings }),
        queryClient.invalidateQueries({ queryKey: ["inventory"] }),
        queryClient.invalidateQueries({ queryKey: qk.dashboard }),
      ])
      toast.success(value.expiryDatesEnabled ? "Expiry dates activated" : "Expiry dates deactivated")
    },
    onError: (error) => toast.error(errorMessage(error)),
  })

  if (query.isLoading) return <FullPageSpinner />
  if (query.error || !query.data) return <ErrorState error={query.error} onRetry={query.refetch} />
  const enabled = query.data.expiryDatesEnabled

  return (
    <>
      <PageHeader title="Feature controls" description="Activate or deactivate optional parts of ÀLara ERP for everyone." />
      <Section title="Inventory" bodyClassName="divide-y divide-hairline">
        <div className="flex flex-col gap-4 p-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex min-w-0 gap-3">
            <span className="grid size-10 shrink-0 place-items-center rounded-md bg-blush text-rose-ink"><CalendarClock className="size-5" /></span>
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="font-semibold">Expiry dates</h2>
                <span className={enabled ? "rounded-full bg-tone-green-bg px-2 py-0.5 text-xs font-semibold text-tone-green" : "rounded-full bg-secondary px-2 py-0.5 text-xs font-semibold text-foreground"}>
                  {enabled ? "Active" : "Inactive"}
                </span>
              </div>
              <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                {enabled
                  ? "Expiry dates are shown across inventory and are required when receiving products configured to track them."
                  : "Expiry dates are hidden across the system and are not required when receiving stock. Existing dates stay saved for later."}
              </p>
            </div>
          </div>
          <Button variant={enabled ? "outline" : "default"} disabled={update.isPending} onClick={() => update.mutate(!enabled)}>
            {update.isPending ? "Saving…" : enabled ? "Deactivate" : "Activate"}
          </Button>
        </div>
      </Section>
    </>
  )
}
