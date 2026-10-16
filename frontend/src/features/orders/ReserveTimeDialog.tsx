import { useState } from "react"
import { Clock } from "lucide-react"
import { Field } from "@/components/common/controls"
import { InlineSpinner } from "@/components/common/states"
import { Button } from "@/components/ui/button"
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { cn } from "@/lib/utils"
import { dateTime } from "@/lib/format"

const QUICK = [
  { label: "24 hours", hours: 24 },
  { label: "3 days", hours: 72 },
  { label: "1 week", hours: 168 },
]

/** yyyy-MM-ddTHH:mm in the browser's local time, for <input type="datetime-local">. */
const toLocalInput = (d: Date) => {
  const pad = (n: number) => String(n).padStart(2, "0")
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/**
 * Asks until when stock should be held. When the time is up an alert is raised (in the app, and on WhatsApp once
 * connected); nothing is released automatically.
 */
export function ReserveTimeDialog({ open, onOpenChange, title, description, confirmLabel, pending, initial, onConfirm }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  confirmLabel: string
  pending?: boolean
  initial?: string | null
  onConfirm: (untilIso: string, notes: string) => void
}) {
  const [value, setValue] = useState(() => toLocalInput(initial ? new Date(initial) : new Date(Date.now() + 24 * 3600_000)))
  const [notes, setNotes] = useState("")
  const until = value ? new Date(value) : null
  const error = !until || Number.isNaN(until.getTime()) ? "Choose a date and time."
    : until.getTime() <= Date.now() ? "Choose a time in the future."
    : until.getTime() > Date.now() + 90 * 24 * 3600_000 ? "At most 90 days." : undefined

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2"><Clock className="size-4 text-rose-ink" /> {title}</DialogTitle>
          <DialogDescription>{description ?? "The stock is held for this customer and not counted as sold. When the time is up you get an alert (and a WhatsApp message once WhatsApp is connected)."}</DialogDescription>
        </DialogHeader>
        <form noValidate className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (!error && until) onConfirm(until.toISOString(), notes.trim()) }}>
          <div className="flex flex-wrap gap-2" role="group" aria-label="Quick choices">
            {QUICK.map((q) => {
              const v = toLocalInput(new Date(Date.now() + q.hours * 3600_000))
              return (
                <button key={q.label} type="button" onClick={() => setValue(v)}
                  className={cn("rounded-full border px-3 py-1 text-xs", value.slice(0, 13) === v.slice(0, 13) ? "border-rose bg-blush text-rose-ink" : "border-hairline hover:bg-accent")}>
                  {q.label}
                </button>
              )
            })}
          </div>
          <Field label="Reserve until" htmlFor="reserve-until" error={error} hint={until && !error ? `Alert on ${dateTime(until.toISOString())} (Beirut time)` : undefined} required>
            <Input id="reserve-until" type="datetime-local" value={value} onChange={(e) => setValue(e.target.value)} />
          </Field>
          <Field label="Note (optional)" htmlFor="reserve-note">
            <Textarea id="reserve-note" rows={2} placeholder="e.g. Customer pays on Friday" value={notes} onChange={(e) => setNotes(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={!!error || pending}>{pending && <InlineSpinner />} {confirmLabel}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
