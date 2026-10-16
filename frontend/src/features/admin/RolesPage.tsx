import { Fragment } from "react"
import { useQuery } from "@tanstack/react-query"
import { Check } from "lucide-react"
import { PageHeader } from "@/components/common/page"
import { ErrorState, FullPageSpinner } from "@/components/common/states"
import { api } from "@/lib/api"
import { qk } from "@/lib/queries"
import type { Role } from "@/types/api"

const groupLabel: Record<string, string> = {
  products: "Catalogue", inventory: "Inventory", warehouses: "Warehouses", orders: "Orders",
  delivery_companies: "Delivery companies", reports: "Reports", costs: "Costs & profit", users: "Employees",
}
const actionLabel = (p: string) => {
  const a = p.split(".")[1]
  return ({ manage_pricing: "change prices", manage_thresholds: "set stock alerts", manage_payments: "manage order payments", assign_roles: "assign roles" } as Record<string, string>)[a] ?? a.replace(/_/g, " ")
}

export function RolesPage() {
  const { data: roles, isLoading, error } = useQuery({ queryKey: qk.roles, queryFn: () => api<Role[]>("/api/roles") })
  const { data: permissions } = useQuery({ queryKey: ["identity", "permissions"], queryFn: () => api<string[]>("/api/roles/permissions") })
  if (isLoading) return <FullPageSpinner />
  if (error || !roles) return <ErrorState error={error} />
  const groups = Object.entries((permissions ?? []).reduce<Record<string, string[]>>((acc, p) => { (acc[p.split(".")[0]] ??= []).push(p); return acc }, {}))

  return (
    <>
      <PageHeader title="Roles & permissions" description="What each role can do. Permissions are enforced by the server on every request; these roles are defined by the system." />
      <div className="overflow-x-auto rounded-lg border border-hairline bg-card">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-hairline">
              <th className="sticky left-0 bg-card px-4 py-3 text-left" />
              {roles.map((r) => (
                <th key={r.name} className="min-w-28 px-3 py-3 text-center align-bottom">
                  <div className="font-semibold">{r.name}</div>
                  <div className="text-xs font-normal text-muted-foreground">{r.userCount} active</div>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {groups.map(([group, perms]) => (
              <Fragment key={group}>
                <tr className="bg-secondary/50"><td colSpan={roles.length + 1} className="eyebrow sticky left-0 px-4 py-1.5">{groupLabel[group] ?? group}</td></tr>
                {perms.map((p) => (
                  <tr key={p} className="border-b border-hairline last:border-0">
                    <td className="sticky left-0 bg-card px-4 py-2 first-letter:uppercase">{actionLabel(p)} <span className="code ml-1 text-muted-foreground">{p}</span></td>
                    {roles.map((r) => (
                      <td key={r.name} className="px-3 py-2 text-center">
                        {r.permissions.includes(p) ? <Check className="mx-auto size-4 text-rose-ink" aria-label="Allowed" /> : <span className="text-hairline" aria-label="Not allowed">—</span>}
                      </td>
                    ))}
                  </tr>
                ))}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
      <p className="mt-3 text-xs text-muted-foreground">{roles.map((r) => `${r.name}: ${r.description}`).join(" · ")}</p>
    </>
  )
}
