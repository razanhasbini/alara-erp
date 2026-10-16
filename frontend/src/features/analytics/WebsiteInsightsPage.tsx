import type { ComponentType } from "react"
import { Camera, Eye, Filter, Repeat2, ShoppingBag, Sparkles, Timer, Users } from "lucide-react"
import { PageHeader } from "@/components/common/page"
import { Pill } from "@/components/common/StatusBadge"

/**
 * Placeholder for storefront analytics. Nothing here is live: the data will come from the new React website
 * (events sent to the ERP) once it launches. Kept explicit so nobody mistakes it for real numbers.
 */
const PLANNED: { icon: ComponentType<{ className?: string }>; title: string; detail: string }[] = [
  { icon: Sparkles, title: "Hair quiz", detail: "How many visitors take the hair-type test, where they drop off, and which routine it recommends most." },
  { icon: Camera, title: "Before & after", detail: "Uploads of before/after photos and how often they lead to a purchase." },
  { icon: ShoppingBag, title: "Add to cart", detail: "Products added to cart, removed, and left in abandoned carts — by product and bundle." },
  { icon: Filter, title: "Conversion funnel", detail: "Visit → product page → cart → checkout → order, per channel and campaign." },
  { icon: Eye, title: "Most viewed", detail: "Which products and bundles are looked at most, and which are viewed but rarely bought." },
  { icon: Users, title: "New vs returning", detail: "First-time visitors compared with customers coming back to reorder." },
  { icon: Repeat2, title: "Reorder timing", detail: "How long customers take to reorder each product, to plan reminders and stock." },
  { icon: Timer, title: "Live visitors", detail: "People on the site right now and what they are looking at." },
]

export function WebsiteInsightsPage() {
  return (
    <>
      <PageHeader title="Website insights" meta={<Pill tone="rose" dot={false}>Coming soon</Pill>}
        description="Visitor behaviour from the new ÀLara Line website. This section turns on when the website launches and starts sending its activity to the ERP." />
      <div className="mb-6 rounded-lg border border-dashed border-petal bg-blush/40 px-5 py-4 text-sm">
        <p className="font-medium">Nothing is being tracked yet.</p>
        <p className="mt-1 text-muted-foreground">
          The current site (WooCommerce) is not connected. When the new website is live, its orders will arrive here automatically
          and the reports below will fill in. No numbers on this page are real until then.
        </p>
      </div>
      <ul className="grid gap-px overflow-hidden rounded-lg border border-hairline bg-hairline sm:grid-cols-2 lg:grid-cols-4">
        {PLANNED.map(({ icon: Icon, title, detail }) => (
          <li key={title} className="bg-card p-4">
            <Icon className="size-5 text-rose" />
            <p className="mt-3 font-medium">{title}</p>
            <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{detail}</p>
          </li>
        ))}
      </ul>
    </>
  )
}
