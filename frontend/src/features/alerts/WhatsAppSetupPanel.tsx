import type { ReactNode } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { CircleAlert, CircleCheck, Send } from "lucide-react"
import { toast } from "sonner"
import { InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { alertKeys } from "@/lib/alerts"
import { api, errorMessage } from "@/lib/api"
import { cn } from "@/lib/utils"
import type { WhatsAppSetup } from "@/types/api"

function Row({ ok, label, children }: { ok: boolean; label: string; children: ReactNode }) {
  const Icon = ok ? CircleCheck : CircleAlert
  return (
    <li className="flex gap-2.5 px-4 py-2.5 text-sm">
      <Icon className={cn("mt-0.5 size-4 shrink-0", ok ? "text-tone-green" : "text-tone-amber")} aria-hidden />
      <div className="min-w-0">
        <p className="font-medium">{label}</p>
        <div className="mt-0.5 leading-5 break-words text-muted-foreground">{children}</div>
      </div>
    </li>
  )
}

/** For the administrator: what's left to connect WhatsApp, and a way to prove it works. Shows setting names, never their values. */
export function WhatsAppSetupPanel() {
  const queryClient = useQueryClient()
  const { data: setup } = useQuery({ queryKey: alertKeys.whatsAppSetup, queryFn: () => api<WhatsAppSetup>("/api/notifications/whatsapp/setup") })
  const test = useMutation({
    mutationFn: () => api<void>("/api/notifications/whatsapp/test", { method: "POST" }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: alertKeys.all }); toast.success("Queued — it will arrive on WhatsApp within a minute") },
    onError: (e) => toast.error(errorMessage(e)),
  })
  if (!setup) return null

  return (
    <section aria-label="WhatsApp connection" className="mb-4 overflow-hidden frame rounded-lg bg-card">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-hairline px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold">WhatsApp connection</h2>
          <p className="text-xs text-muted-foreground">{setup.connected ? "Alerts are sent to the numbers below." : "Alerts are only kept on this page until this is finished."}</p>
        </div>
        <Button size="sm" variant="outline" disabled={!setup.connected || test.isPending} onClick={() => test.mutate()}>
          {test.isPending ? <InlineSpinner /> : <Send />} Send test message
        </Button>
      </div>
      <ul className="divide-y divide-hairline">
        <Row ok={setup.connected} label="Connection">
          {setup.connected ? `Connected, using Meta's API ${setup.apiVersion}.` : <>Still needed in Railway: <span className="code">{setup.missingSettings.join(", ")}</span></>}
        </Row>
        <Row ok={setup.recipients.length > 0 && setup.ignoredRecipients === 0} label="Who gets the alerts">
          {setup.recipients.length > 0 ? <span className="tabular">{setup.recipients.join(", ")}</span> : "No numbers yet."}
          {setup.ignoredRecipients > 0 && <p>{setup.ignoredRecipients} number{setup.ignoredRecipients === 1 ? " is" : "s are"} ignored. Write them with the country code and no leading 0, like 96170123456.</p>}
        </Row>
        <Row ok={!!setup.templateName} label="Message template">
          {setup.templateName ? `${setup.templateName} (${setup.templateLanguage})` : <>Not set, so alerts only reach numbers that messaged the business in the last 24 hours. Set <span className="code">WhatsApp__TemplateName</span>.</>}
        </Row>
        <Row ok={setup.deliveryReportsReady} label="Delivery reports">
          {setup.deliveryReportsReady ? "On, so alerts show when a message is delivered, read or failed." : <>Off, so “Sent” only means Meta accepted the message. Set <span className="code">WhatsApp__AppSecret</span> and <span className="code">WhatsApp__WebhookVerifyToken</span>.</>}
          <p>Callback URL for Meta: <span className="code">{window.location.origin}{setup.webhookPath}</span></p>
        </Row>
        <Row ok={setup.linksReady} label="Links in messages">
          {setup.linksReady ? "Each message links back to the ERP." : <>Messages won't include a link. Set <span className="code">App__FrontendUrl</span>.</>}
        </Row>
      </ul>
    </section>
  )
}
