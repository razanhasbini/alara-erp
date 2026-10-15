import { lazy, Suspense } from "react"
import { createBrowserRouter, Outlet } from "react-router"
import { FullPageSpinner } from "@/components/common/states"
import { RequireAuth, RequirePermission } from "@/auth/guards"
import { AppShell } from "@/components/layout/AppShell"
import { EmployeesPage } from "@/features/admin/EmployeesPage"
import { RolesPage } from "@/features/admin/RolesPage"
import { LoginPage } from "@/features/auth/LoginPage"
import { ProfilePage } from "@/features/auth/ProfilePage"
import { NotFoundPage, UnauthorizedPage } from "@/features/auth/StatusPages"
import { CategoriesPage } from "@/features/catalog/CategoriesPage"
import { ProductDetailPage } from "@/features/catalog/ProductDetailPage"
import { ProductFormPage } from "@/features/catalog/ProductFormPage"
import { ProductsPage } from "@/features/catalog/ProductsPage"
import { MovementsPage } from "@/features/inventory/MovementsPage"
import { NewReceiptPage, ReceiptDetailPage, ReceiptsPage } from "@/features/inventory/ReceiptsPages"
import { StockPage } from "@/features/inventory/StockPage"
import { NewTransferPage, TransferDetailPage, TransfersPage } from "@/features/inventory/TransfersPages"
import { WarehousesPage } from "@/features/inventory/WarehousesPage"
import { DeliveriesPage } from "@/features/orders/DeliveriesPage"
import { OrderDetailPage } from "@/features/orders/OrderDetailPage"
import { OrderFormPage } from "@/features/orders/OrderFormPage"
import { OrdersPage } from "@/features/orders/OrdersPage"
import { P } from "@/lib/permissions"

// Charts (recharts) are only needed on the dashboard; keep them out of the main bundle.
const DashboardPage = lazy(() => import("@/features/dashboard/DashboardPage").then((m) => ({ default: m.DashboardPage })))

/** Route group gated by a permission (UX only — the API enforces the same permission). */
const gate = (permission: string, children: { path: string; element: React.ReactNode }[]) => ({
  element: <RequirePermission permission={permission} />, children,
})

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    element: <RequireAuth />,
    children: [{
      element: <AppShell />,
      children: [
        { index: true, element: <Suspense fallback={<FullPageSpinner />}><DashboardPage /></Suspense> },
        { path: "profile", element: <ProfilePage /> },
        { path: "unauthorized", element: <UnauthorizedPage /> },

        gate(P.orders.view, [
          { path: "orders", element: <OrdersPage /> },
          { path: "orders/:id", element: <OrderDetailPage /> },
          { path: "deliveries", element: <DeliveriesPage /> },
        ]),
        gate(P.orders.create, [{ path: "orders/new", element: <OrderFormPage /> }]),
        gate(P.orders.update, [{ path: "orders/:id/edit", element: <OrderFormPage /> }]),

        gate(P.inventory.view, [
          { path: "inventory", element: <StockPage /> },
          { path: "inventory/receipts", element: <ReceiptsPage /> },
          { path: "inventory/receipts/:id", element: <ReceiptDetailPage /> },
          { path: "inventory/transfers", element: <TransfersPage /> },
          { path: "inventory/transfers/:id", element: <TransferDetailPage /> },
          { path: "inventory/movements", element: <MovementsPage /> },
          { path: "warehouses", element: <WarehousesPage /> },
        ]),
        gate(P.inventory.receive, [{ path: "inventory/receipts/new", element: <NewReceiptPage /> }]),
        gate(P.inventory.transfer, [{ path: "inventory/transfers/new", element: <NewTransferPage /> }]),

        gate(P.products.view, [
          { path: "products", element: <ProductsPage /> },
          { path: "products/bundles", element: <ProductsPage type="Bundle" /> },
          { path: "products/:id", element: <ProductDetailPage /> },
          { path: "categories", element: <CategoriesPage /> },
        ]),
        gate(P.products.create, [{ path: "products/new", element: <ProductFormPage /> }]),
        gate(P.products.update, [{ path: "products/:id/edit", element: <ProductFormPage /> }]),

        gate(P.users.view, [
          { path: "employees", element: <EmployeesPage /> },
          { path: "roles", element: <RolesPage /> },
        ]),

        { path: "*", element: <NotFoundPage /> },
      ],
    }],
  },
  { path: "*", element: <Outlet /> },
])
