import { useState, type ComponentType } from "react"
import { NavLink, Outlet, useLocation, useNavigate } from "react-router"
import {
  ArrowLeftRight, Boxes, ChevronDown, ClipboardList, FolderTree, History, KeyRound, LayoutDashboard, LogOut, Menu,
  Package, PackageOpen, ShieldCheck, Truck, UserRound, Users, Warehouse,
} from "lucide-react"
import monogram from "@/assets/monogram.png"
import { useAuth } from "@/auth/auth"
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Button } from "@/components/ui/button"
import { P } from "@/lib/permissions"
import { cn } from "@/lib/utils"

interface NavItem { to: string; label: string; icon: ComponentType<{ className?: string }>; permission?: string; end?: boolean }

const NAV: { group: string; items: NavItem[] }[] = [
  { group: "", items: [{ to: "/", label: "Dashboard", icon: LayoutDashboard, end: true }] },
  {
    group: "Sales",
    items: [
      { to: "/orders", label: "Orders", icon: ClipboardList, permission: P.orders.view },
      { to: "/deliveries", label: "Deliveries & cash", icon: Truck, permission: P.orders.view },
    ],
  },
  {
    group: "Stock",
    items: [
      { to: "/inventory", label: "Inventory", icon: Boxes, permission: P.inventory.view, end: true },
      { to: "/inventory/receipts", label: "Receipts", icon: PackageOpen, permission: P.inventory.view },
      { to: "/inventory/transfers", label: "Transfers", icon: ArrowLeftRight, permission: P.inventory.view },
      { to: "/inventory/movements", label: "Stock movements", icon: History, permission: P.inventory.view },
      { to: "/warehouses", label: "Warehouses", icon: Warehouse, permission: P.inventory.view },
    ],
  },
  {
    group: "Catalogue",
    items: [
      { to: "/products", label: "Products", icon: Package, permission: P.products.view, end: true },
      { to: "/products/bundles", label: "Bundles", icon: Boxes, permission: P.products.view },
      { to: "/categories", label: "Categories", icon: FolderTree, permission: P.products.view },
    ],
  },
  {
    group: "Administration",
    items: [
      { to: "/employees", label: "Employees", icon: Users, permission: P.users.view },
      { to: "/roles", label: "Roles & permissions", icon: ShieldCheck, permission: P.users.view },
    ],
  },
]

function Brand() {
  return (
    <div className="flex items-center gap-2.5 px-4 pt-5 pb-4">
      <img src={monogram} alt="" className="size-8 rounded-md bg-blush object-contain p-0.5" />
      <div className="leading-tight">
        <div className="font-display text-lg font-semibold">ÀLara Line</div>
        <div className="text-[0.66rem] tracking-[0.18em] text-muted-foreground uppercase">Operations</div>
      </div>
    </div>
  )
}

function Nav({ onNavigate }: { onNavigate?: () => void }) {
  const { can } = useAuth()
  return (
    <nav aria-label="Main" className="flex-1 overflow-y-auto px-2 pb-4">
      {NAV.map(({ group, items }) => {
        const visible = items.filter((i) => !i.permission || can(i.permission))
        if (visible.length === 0) return null
        return (
          <div key={group || "home"} className="mt-3 first:mt-0">
            {group && <div className="eyebrow px-2.5 pb-1.5 text-[0.62rem]">{group}</div>}
            <ul className="grid gap-px">
              {visible.map(({ to, label, icon: Icon, end }) => (
                <li key={to}>
                  <NavLink to={to} end={end} onClick={onNavigate}
                    className={({ isActive }) => cn(
                      "relative flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-[0.86rem] transition-colors",
                      isActive
                        ? "bg-blush font-medium text-foreground before:absolute before:top-1.5 before:bottom-1.5 before:-left-2 before:w-[3px] before:rounded-full before:bg-rose"
                        : "text-muted-foreground hover:bg-accent hover:text-foreground",
                    )}>
                    <Icon className="size-4 shrink-0" />
                    {label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        )
      })}
    </nav>
  )
}

function UserMenu() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  if (!user) return null
  const initials = (user.firstName[0] ?? "") + (user.lastName[0] ?? "")
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button className="flex items-center gap-2.5 rounded-md py-1 pr-1.5 pl-1 text-left hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none">
          <span className="grid size-7 place-items-center rounded-full bg-foreground text-[0.7rem] font-semibold text-background">{initials.toUpperCase()}</span>
          <span className="hidden leading-tight sm:block">
            <span className="block text-sm font-medium">{user.firstName} {user.lastName}</span>
            <span className="block text-[0.7rem] text-muted-foreground">{user.roles.join(", ") || "No role"}</span>
          </span>
          <ChevronDown className="size-3.5 text-muted-foreground" />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel className="font-normal">
          <div className="text-sm font-medium">{user.firstName} {user.lastName}</div>
          <div className="truncate text-xs text-muted-foreground">{user.email}</div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => navigate("/profile")}><UserRound /> Profile</DropdownMenuItem>
        <DropdownMenuItem onSelect={() => navigate("/profile#password")}><KeyRound /> Change password</DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={async () => { await logout(); navigate("/login", { replace: true }) }}>
          <LogOut /> Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

export function AppShell() {
  const [open, setOpen] = useState(false)
  const location = useLocation()
  return (
    <div className="min-h-svh lg:grid lg:grid-cols-[15rem_1fr]">
      <aside className="sticky top-0 hidden h-svh flex-col border-r border-hairline bg-sidebar lg:flex">
        <Brand />
        <Nav />
      </aside>

      <Sheet open={open} onOpenChange={setOpen}>
        <SheetContent side="left" className="w-64 bg-sidebar p-0">
          <SheetTitle className="sr-only">Navigation</SheetTitle>
          <div className="flex h-full flex-col"><Brand /><Nav onNavigate={() => setOpen(false)} /></div>
        </SheetContent>
      </Sheet>

      <div className="flex min-w-0 flex-col">
        <header className="sticky top-0 z-20 flex h-12 items-center justify-between gap-3 border-b border-hairline bg-background/90 px-4 backdrop-blur-sm md:px-6">
          <div className="flex items-center gap-2 lg:hidden">
            <Button variant="ghost" size="icon-sm" aria-label="Open navigation" onClick={() => setOpen(true)}><Menu /></Button>
            <img src={monogram} alt="ÀLara Line" className="size-6 object-contain" />
          </div>
          <div className="hidden lg:block" />
          <UserMenu />
        </header>
        <main key={location.pathname.split("/")[1]} className="mx-auto w-full max-w-[1320px] flex-1 px-4 py-6 md:px-6 lg:px-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
