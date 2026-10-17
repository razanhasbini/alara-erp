import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { cn } from "@/lib/utils"
import { COUNTRY_CODES, joinPhone, OTHER_CODE as OTHER, splitPhone } from "@/lib/phone"

/**
 * Country code (Lebanon by default) + the local number, so staff type only "70 123 456".
 * The value is the full international number, e.g. "+961 70 123 456". "Other" accepts any full number.
 */
export function PhoneInput({ id, value, onChange, invalid, autoFocus, className }: {
  id: string
  value: string
  onChange: (value: string) => void
  invalid?: boolean
  autoFocus?: boolean
  className?: string
}) {
  const { code, number } = splitPhone(value)
  return (
    <div className={cn("flex gap-1.5", className)}>
      <Select value={code} onValueChange={(next) => onChange(next === OTHER ? (number.startsWith("+") ? number : "") : joinPhone(next, code === OTHER ? number.replace(/^\+\d*\s*/, "") : number))}>
        <SelectTrigger className="w-24 shrink-0 tabular" aria-label="Country code"><SelectValue>{code === OTHER ? "Other" : code}</SelectValue></SelectTrigger>
        <SelectContent>
          {COUNTRY_CODES.map((c) => <SelectItem key={c.code} value={c.code}><span className="tabular">{c.code}</span> <span className="text-muted-foreground">{c.country}</span></SelectItem>)}
          <SelectItem value={OTHER}>Other</SelectItem>
        </SelectContent>
      </Select>
      <Input id={id} type="tel" inputMode="tel" autoFocus={autoFocus} aria-invalid={invalid}
        placeholder={code === OTHER ? "+44 20 7946 0000" : code === "+961" ? "70 123 456" : "Phone number"}
        value={number} onChange={(e) => onChange(code === OTHER ? e.target.value : joinPhone(code, e.target.value))} />
    </div>
  )
}
