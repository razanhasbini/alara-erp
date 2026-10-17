import { describe, expect, it } from "vitest"
import { formatPhone, joinPhone, splitPhone } from "@/lib/phone"

describe("phone input", () => {
  it("defaults to Lebanon and keeps the typed spacing", () => {
    expect(splitPhone("")).toEqual({ code: "+961", number: "" })
    expect(splitPhone("+961 70 123 456")).toEqual({ code: "+961", number: "70 123 456" })
    expect(splitPhone("+96170123456")).toEqual({ code: "+961", number: "70123456" })
    expect(splitPhone("70 123 456")).toEqual({ code: "+961", number: "70 123 456" }) // older numbers saved without a code
  })
  it("recognises other countries and drops the local trunk 0", () => {
    expect(splitPhone("+962 79 123 4567")).toEqual({ code: "+962", number: "79 123 4567" })
    expect(joinPhone("+961", "03 123 456")).toBe("+961 3 123 456")
    expect(joinPhone("+962", "")).toBe("+962 ")
    expect(joinPhone("+961", "")).toBe("")
  })
  it("keeps spaces typed between groups and formats for display", () => {
    expect(splitPhone(joinPhone("+961", "70 "))).toEqual({ code: "+961", number: "70 " })
    expect(formatPhone("+96170123456")).toBe("+961 70 123 456")
    expect(formatPhone("+9613123456")).toBe("+961 3 123 456")
    expect(formatPhone("70123456")).toBe("+961 70 123 456")
    expect(formatPhone("+962791234567")).toBe("+962 791234567")
  })
})
