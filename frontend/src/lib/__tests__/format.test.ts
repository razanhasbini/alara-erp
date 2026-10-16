import { describe, expect, it } from "vitest"
import { dateOnly, humanize, money } from "@/lib/format"
import { ApiError, buildQuery, errorMessage } from "@/lib/api"

describe("money", () => {
  it("formats USD with two decimals and handles empty values", () => {
    expect(money(1234.5)).toBe("$1,234.50")
    expect(money(0)).toBe("$0.00")
    expect(money(null)).toBe("—")
  })
})

describe("dateOnly", () => {
  it("shows a calendar date without shifting it across time zones", () => {
    expect(dateOnly("2027-03-01")).toBe("01 Mar 2027")
  })
})

describe("humanize", () => {
  it("turns enum names into readable labels", () => {
    expect(humanize("CashOnDelivery")).toBe("Cash on delivery")
    expect(humanize("WhatsApp")).toBe("WhatsApp")
  })
})

describe("buildQuery", () => {
  it("drops empty values and repeats array parameters", () => {
    expect(buildQuery({ search: "", page: 2, statuses: ["Draft", "Confirmed"], x: undefined }))
      .toBe("?page=2&statuses=Draft&statuses=Confirmed")
    expect(buildQuery({})).toBe("")
  })
})

describe("errorMessage", () => {
  it("prefers the first field error for validation problems", () => {
    const e = new ApiError(400, "validation_failed", "Validation failed", "One or more fields are invalid.", { phone: ["Enter a valid phone number."] })
    expect(errorMessage(e)).toBe("Enter a valid phone number.")
  })
  it("uses the problem detail for business errors", () => {
    expect(errorMessage(new ApiError(422, "inventory.insufficient_stock", "x", "Not enough stock of Mask."))).toBe("Not enough stock of Mask.")
  })
})
