import { lazy, Suspense } from "react"
import { createBrowserRouter, Navigate, Outlet } from "react-router"
import { useAuth } from "@/auth/auth"
import { FullPageSpinner } from "@/components/common/states"
import { RequireAuth, RequirePermission, RequireRole } from "@/auth/guards"
import { AppShell } from "@/components/layout/AppShell"
import { EmployeesPage } from "@/features/admin/EmployeesPage"
import { RolesPage } from "@/features/admin/RolesPage"
import { FeatureSettingsPage } from "@/features/admin/FeatureSettingsPage"
import { LoginPage } from "@/features/auth/LoginPage"
import { ProfilePage } from "@/features/auth/ProfilePage"
import { NotFoundPage, RouteErrorPage } from "@/features/auth/StatusPages"
import { CategoriesPage } from "@/features/catalog/CategoriesPage"
import { ProductDetailPage } from "@/features/catalog/ProductDetailPage"
import { ProductFormPage } from "@/features/catalog/ProductFormPage"
import { ProductsPage } from "@/features/catalog/ProductsPage"
import { CustomersPage } from "@/features/customers/CustomersPage"
import { MovementsPage } from "@/features/inventory/MovementsPage"
import { NewReceiptPage, ReceiptDetailPage, ReceiptsPage } from "@/features/inventory/ReceiptsPages"
import { StockPage } from "@/features/inventory/StockPage"
import { NewTransferPage, TransferDetailPage, TransfersPage } from "@/features/inventory/TransfersPages"
import { WarehousesPage } from "@/features/inventory/WarehousesPage"
import { AlertsPage } from "@/features/alerts/AlertsPage"
import { WebsiteInsightsPage } from "@/features/analytics/WebsiteInsightsPage"
import { BillsPage } from "@/features/finance/BillsPage"
import { OrderDetailPage } from "@/features/orders/OrderDetailPage"
import { OrderFormPage } from "@/features/orders/OrderFormPage"
import { OrdersPage } from "@/features/orders/OrdersPage"
import { P } from "@/lib/permissions"

// Charts (recharts) are only needed on reporting pages; keep them out of the main bundle.
const DashboardPage = lazy(() => import("@/features/dashboard/DashboardPage").then((m) => ({ default: m.DashboardPage })))
const AnalyticsPage = lazy(() => import("@/features/analytics/AnalyticsPage").then((m) => ({ default: m.AnalyticsPage })))

function HomePage() {
  const { can } = useAuth()
  return can(P.reports.view)
    ? <Suspense fallback={<FullPageSpinner />}><DashboardPage /></Suspense>
    : <Navigate to="/inventory" replace />
}

/** Route group gated by a permission (UX only — the API enforces the same permission). */
const gate = (permission: string, children: { path: string; element: React.ReactNode }[]) => ({
  element: <RequirePermission permission={permission} />, children,
})

export const router = createBrowserRouter([
  { path: "/login", element: <LoginPage />, errorElement: <RouteErrorPage /> },
  {
    element: <RequireAuth />,
    errorElement: <RouteErrorPage />,
    children: [{
      element: <AppShell />,
      children: [
        { index: true, element: <HomePage /> },
        { path: "profile", element: <ProfilePage /> },
        { path: "alerts", element: <AlertsPage /> },
        { path: "unauthorized", element: <Navigate to="/" replace /> },

        gate(P.orders.view, [
          { path: "orders", element: <OrdersPage /> },
          { path: "customers", element: <CustomersPage /> },
          { path: "orders/:id", element: <OrderDetailPage /> },
        ]),
        gate(P.orders.create, [
          { path: "orders/new", element: <OrderFormPage /> },
        ]),
        gate(P.orders.update, [{ path: "orders/:id/edit", element: <OrderFormPage /> }]),
        gate(P.expenses.manage, [{ path: "bills", element: <BillsPage /> }]),

        gate(P.inventory.view, [
          { path: "inventory", element: <StockPage /> },
          { path: "inventory/receipts", element: <ReceiptsPage /> },
          { path: "inventory/receipts/:id", element: <ReceiptDetailPage /> },
          { path: "inventory/movements", element: <MovementsPage /> },
          { path: "warehouses", element: <WarehousesPage /> },
        ]),
        gate(P.inventory.receive, [{ path: "inventory/receipts/new", element: <NewReceiptPage /> }]),
        gate(P.inventory.transfer, [
          { path: "inventory/transfers", element: <TransfersPage /> },
          { path: "inventory/transfers/:id", element: <TransferDetailPage /> },
          { path: "inventory/transfers/new", element: <NewTransferPage /> },
        ]),

        gate(P.products.view, [
          { path: "products", element: <ProductsPage type="Individual" /> },
          { path: "products/bundles", element: <ProductsPage type="Bundle" /> },
          { path: "products/:id", element: <ProductDetailPage /> },
          { path: "categories", element: <CategoriesPage /> },
        ]),
        gate(P.products.create, [{ path: "products/new", element: <ProductFormPage /> }]),
        gate(P.products.update, [{ path: "products/:id/edit", element: <ProductFormPage /> }]),

        gate(P.reports.view, [
          { path: "analytics", element: <Suspense fallback={<FullPageSpinner />}><AnalyticsPage /></Suspense> },
          { path: "website-insights", element: <WebsiteInsightsPage /> },
        ]),

        gate(P.users.view, [
          { path: "employees", element: <EmployeesPage /> },
          { path: "roles", element: <RolesPage /> },
        ]),
        { element: <RequireRole role="Super Admin" />, children: [
          { path: "features", element: <FeatureSettingsPage /> },
        ] },

        { path: "*", element: <NotFoundPage /> },
      ],
    }],
  },
  { path: "*", element: <Outlet /> },
])
