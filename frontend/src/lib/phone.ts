/** Lebanon first (the default); the rest are where orders most often come from. */
export const COUNTRY_CODES: { code: string; country: string }[] = [
  { code: "+961", country: "Lebanon" },
  { code: "+962", country: "Jordan" },
  { code: "+963", country: "Syria" },
  { code: "+964", country: "Iraq" },
  { code: "+965", country: "Kuwait" },
  { code: "+966", country: "Saudi Arabia" },
  { code: "+968", country: "Oman" },
  { code: "+971", country: "UAE" },
  { code: "+973", country: "Bahrain" },
  { code: "+974", country: "Qatar" },
  { code: "+20", country: "Egypt" },
  { code: "+33", country: "France" },
  { code: "+49", country: "Germany" },
  { code: "+44", country: "UK" },
  { code: "+1", country: "USA / Canada" },
  { code: "+61", country: "Australia" },
]
export const DEFAULT_COUNTRY_CODE = "+961"
export const OTHER_CODE = "other"
const OTHER = OTHER_CODE

/** Splits a stored number into its country code and the rest. Numbers without "+" are local Lebanese numbers. */
export function splitPhone(value: string): { code: string; number: string } {
  const trimmed = value.replace(/^\s+/, "")
  if (!trimmed.startsWith("+")) return { code: DEFAULT_COUNTRY_CODE, number: trimmed }
  const digits = trimmed.replace(/[^\d]/g, "")
  const match = [...COUNTRY_CODES].sort((a, b) => b.code.length - a.code.length).find((c) => digits.startsWith(c.code.slice(1)))
  if (!match) return { code: OTHER, number: trimmed }
  // Keep the user's spacing for the local part when the stored value had it ("+961 70 123 456").
  const rest = trimmed.slice(1).replace(/^\s*/, "")
  const afterCode = rest.startsWith(match.code.slice(1)) ? rest.slice(match.code.length - 1).replace(/^\s+/, "") : digits.slice(match.code.length - 1)
  return { code: match.code, number: afterCode }
}

/** "+961" + "03 123 456" → "+961 3 123 456": the local trunk 0 is dropped after a country code. */
export function joinPhone(code: string, number: string): string {
  // Only leading spaces are dropped, so a space typed between groups ("70 1…") survives while typing.
  const local = number.replace(/^\s+/, "")
  if (code === OTHER) return local
  const withoutTrunk = local.replace(/^0+/, "")
  // An empty number keeps a non-default code chosen, so picking "+962" first doesn't snap back to Lebanon.
  if (!withoutTrunk) return code === DEFAULT_COUNTRY_CODE ? "" : `${code} `
  return `${code} ${withoutTrunk}`
}

/** For display: "+96170123456" → "+961 70 123 456"; other countries → "+962 791234567". */
export function formatPhone(value: string | null | undefined): string {
  if (!value) return ""
  const { code, number } = splitPhone(value)
  if (code === OTHER) return value
  const digits = number.replace(/\D/g, "")
  if (code === "+961" && (digits.length === 7 || digits.length === 8)) {
    const head = digits.length === 8 ? 2 : 1
    return `+961 ${digits.slice(0, head)} ${digits.slice(head, head + 3)} ${digits.slice(head + 3)}`
  }
  return digits ? `${code} ${digits}` : value
}
