/**
 * Browser smoke test: drives the real UI through the main business flow and fails on any console error
 * or failed API call. It CREATES DATA, so run it only against a backend pointed at alara_erp_test:
 *
 *   ALARA_API_URL=http://localhost:5191 npm run dev     # Vite proxying to the test API
 *   E2E_EMAIL=… E2E_PASSWORD=… CHROME_PATH=… npm run e2e
 *
 * Screenshots go to e2e/shots/ (git-ignored).
 */
import puppeteer from "puppeteer-core"
const BASE = process.env.E2E_BASE ?? "http://localhost:5173"
const EMAIL = process.env.E2E_EMAIL ?? "e2e.admin@alaraline.test"
const PASSWORD = process.env.E2E_PASSWORD
if (!PASSWORD) { console.error("Set E2E_PASSWORD (the test-database admin password)."); process.exit(2) }
import { mkdirSync } from "node:fs"
mkdirSync("e2e/shots", { recursive: true })
const browser = await puppeteer.launch({ executablePath: process.env.CHROME_PATH ?? "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", headless: true, args: ["--no-first-run"] })
const page = await browser.newPage()
await page.setViewport({ width: 1440, height: 900, deviceScaleFactor: 1 })
const problems = []
page.on("console", (m) => { if (m.type() === "error") problems.push("console: " + m.text()) })
page.on("pageerror", (e) => problems.push("pageerror: " + e.message))
page.on("response", (r) => { if (r.url().includes("/api/") && r.status() >= 400 && !r.url().endsWith("/api/auth/me")) problems.push(`HTTP ${r.status()} ${r.request().method()} ${r.url()}`) })

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const shot = async (name) => { await sleep(500); await page.screenshot({ path: `e2e/shots/${name}.png`, fullPage: true }); console.log("shot", name) }
const clickText = async (text, sel = "button, a, [role=option], [role=menuitem]") => {
  const el = await page.waitForSelector(`${sel.split(",").map((s) => `${s.trim()}::-p-text(${text})`).join(",")}`, { timeout: 8000 })
  await el.click()
}
const pick = async (trigger, search) => {
  await clickText(trigger)
  await page.waitForSelector("[cmdk-input]")
  await page.type("[cmdk-input]", search)
  await sleep(700)
  await page.waitForSelector("[cmdk-item]")
  await page.click("[cmdk-item]")
  await sleep(400)
}

// 1. sign in
await page.goto(`${BASE}/login`, { waitUntil: "networkidle0" })
await shot("01-login")
await page.type("#email", EMAIL)
await page.type("#password", PASSWORD)
await clickText("Sign in", "button")
await page.waitForSelector("h1::-p-text(Lara)", { timeout: 10000 })
await shot("02-dashboard-empty")

// 2. catalogue
await page.goto(`${BASE}/products`, { waitUntil: "networkidle0" }); await shot("03-products")
await page.goto(`${BASE}/products/bundles`, { waitUntil: "networkidle0" }); await shot("04-bundles")
await clickText("Full Treatment Set – Oh Honey", "tr *")
await page.waitForSelector("h2::-p-text(In this bundle)"); await shot("05-bundle-detail")

// 3. receive the five components
await page.goto(`${BASE}/inventory/receipts/new`, { waitUntil: "networkidle0" })
for (const product of ["Intensive Repair Shampoo – Oh Honey", "Hydrating Conditioner – Oh Honey", "Leave In Your Dreams", "Miracle Mask", "Miracle Pro-Edition"]) {
  await pick("Add product to this delivery", product)
}
const qtyInputs = await page.$$("input[id^='q-']")
const expInputs = await page.$$("input[id^='e-']")
const costInputs = await page.$$("input[id^='c-']")
for (let i = 0; i < qtyInputs.length; i++) {
  await qtyInputs[i].type(String(20 + i * 5))
  await expInputs[i]?.evaluate((el, v) => { const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set; set.call(el, v); el.dispatchEvent(new Event("input", { bubbles: true })) }, `2027-0${3 + (i % 5)}-15`)
  await costInputs[i]?.type("4.5")
}
await page.type("#rc-sup", "Beauty Labs Istanbul"); await page.type("#rc-ref", "INV-2026-0912")
await shot("06-receive-form")
await clickText("Record receipt", "button")
await page.waitForSelector("h1 ::-p-text(RC-0)", { timeout: 10000 }); await shot("07-receipt")

// 4. an order for the bundle
await page.goto(`${BASE}/orders/new`, { waitUntil: "networkidle0" })
await page.type("#o-name", "Rima Saad"); await page.type("#o-phone", "+961 70 123 456"); await page.type("#o-city", "Beirut")
await page.type("#o-address", "Hamra, Bliss Street, Bldg 12, 3rd floor")
await pick("Add bundle", "Full Treatment Set – Oh")
await pick("Add product", "Miracle Mask")
await shot("08-order-form")
// Reserve first (asks for a time), then mark the reservation as sold.
await clickText("Reserve…", "button")
await page.waitForSelector("[role=dialog] ::-p-text(Reserve stock for this order)"); await clickText("3 days", "[role=dialog] button")
await shot("09a-reserve-dialog")
await clickText("Reserve", "[role=dialog] button[type=submit]")
await page.waitForSelector("header ::-p-text(Reserved)", { timeout: 10000 }); await shot("09b-order-reserved")
await clickText("Mark as sold", "header button")
await clickText("Mark as sold", "[role=alertdialog] button")
await page.waitForSelector("header ::-p-text(Sold)", { timeout: 10000 }); await shot("09-order-sold")
const orderUrl = page.url()
await page.goto(`${BASE}/orders?status=Reserved`, { waitUntil: "networkidle0" }); await shot("09c-reserved-section")
await page.goto(`${BASE}/alerts`, { waitUntil: "networkidle0" }); await page.waitForSelector("h1 ::-p-text(Alerts)"); await shot("09d-alerts")
await page.goto(orderUrl, { waitUntil: "networkidle0" })

// 5. a bill (it lowers net profit in analytics)
await page.goto(`${BASE}/bills`, { waitUntil: "networkidle0" })
await clickText("Add bill", "button")
await page.waitForSelector("#bill-amount"); await page.type("#bill-amount", "45.50"); await page.type("#bill-description", "Generator")
await shot("10-bill-dialog")
await clickText("Add bill", "[role=dialog] button[type=submit]")
await page.waitForSelector("td ::-p-text(Generator)", { timeout: 10000 }); await shot("10a-bills")

// 5b. analytics, bundle withdrawal, website placeholder
await page.goto(`${BASE}/analytics?range=today`, { waitUntil: "networkidle0" })
await page.waitForSelector("::-p-text(Gross profit)", { timeout: 10000 })
const grossProfitShown = await page.evaluate(() => [...document.querySelectorAll(".eyebrow")].some((e) => e.textContent?.includes("Gross profit")))
if (!grossProfitShown) problems.push("analytics: gross profit figure missing")
const netProfitShown = await page.evaluate(() => [...document.querySelectorAll(".eyebrow")].some((e) => e.textContent?.includes("Net profit")))
if (!netProfitShown) problems.push("analytics: net profit figure missing")
await shot("10b-analytics")
await clickText("Bundles", "button[role=radio]"); await sleep(400); await shot("10c-analytics-bundles")
await page.goto(`${BASE}/products/bundles`, { waitUntil: "networkidle0" })
await clickText("Full Treatment Set – Oh Honey", "tr *")
await clickText("Take out of stock", "button")
await page.waitForSelector("#wb-qty"); await shot("10d-withdraw-dialog")
await clickText("Take out 1 set", "button[type=submit]")
await page.waitForSelector("::-p-text(taken out)", { timeout: 10000 }); await shot("10e-bundle-after-withdraw")
await page.goto(`${BASE}/website-insights`, { waitUntil: "networkidle0" }); await shot("10f-website-soon")

// 6. views
for (const [path, name] of [["/inventory", "11-inventory"], ["/inventory/movements", "12-movements"], ["/orders", "13-orders"],
  ["/bills", "14-bills"], ["/", "15-dashboard"], ["/employees", "16-employees"], ["/roles", "17-roles"], ["/warehouses", "18-warehouses"]]) {
  await page.goto(`${BASE}${path}`, { waitUntil: "networkidle0" }); await shot(name)
}
await page.goto(`${BASE}/inventory?product=` + await page.evaluate(async () => {
  const r = await fetch("/api/products?search=Miracle%20Mask"); return (await r.json()).items[0].id }), { waitUntil: "networkidle0" })
await shot("19-stock-sheet")

// phone width
await page.setViewport({ width: 390, height: 844, deviceScaleFactor: 2 })
await page.goto(`${BASE}/orders`, { waitUntil: "networkidle0" }); await shot("20-mobile-orders")
await page.goto(`${BASE}/`, { waitUntil: "networkidle0" }); await shot("21-mobile-dashboard")
const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
console.log("mobile horizontal overflow:", overflow)

// The anonymous session probe before sign-in is expected to 401.
const real = problems.filter((p) => !p.includes("status of 401"))
console.log(real.length ? "PROBLEMS:\n" + real.join("\n") : "no console errors or failed API calls")
await browser.close()
if (real.length || overflow) process.exit(1)
