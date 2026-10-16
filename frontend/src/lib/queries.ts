import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { api } from "@/lib/api"
import type { Availability, Category, DeliveryCompany, ErpSettings, Warehouse } from "@/types/api"

/** Query keys grouped by module so mutations can invalidate whole areas. */
export const qk = {
  settings: ["settings"] as const,
  features: ["settings", "features"] as const,
  dashboard: ["dashboard"] as const,
  categories: (includeInactive = false) => ["catalog", "categories", includeInactive] as const,
  products: (params: object) => ["catalog", "products", params] as const,
  product: (id: string) => ["catalog", "product", id] as const,
  warehouses: (includeInactive = false) => ["inventory", "warehouses", includeInactive] as const,
  stock: (params: object) => ["inventory", "stock", params] as const,
  productStock: (id: string) => ["inventory", "product", id] as const,
  availability: (ids: string[], warehouseId?: string) => ["inventory", "availability", [...ids].sort().join(","), warehouseId ?? ""] as const,
  movements: (params: object) => ["inventory", "movements", params] as const,
  receipts: (params: object) => ["inventory", "receipts", params] as const,
  receipt: (id: string) => ["inventory", "receipt", id] as const,
  transfers: (params: object) => ["inventory", "transfers", params] as const,
  transfer: (id: string) => ["inventory", "transfer", id] as const,
  orders: (params: object) => ["sales", "orders", params] as const,
  order: (id: string) => ["sales", "order", id] as const,
  customers: (params: object) => ["sales", "customers", params] as const,
  customer: (id: string) => ["sales", "customer", id] as const,
  deliveryCompanies: (includeInactive = false) => ["sales", "delivery-companies", includeInactive] as const,
  deliveryExpenses: (params: object) => ["sales", "delivery-expenses", params] as const,
  users: (params: object) => ["identity", "users", params] as const,
  roles: ["identity", "roles"] as const,
  analytics: (kind: string, params: object) => ["analytics", kind, params] as const,
}

export const useSettings = () =>
  useQuery({ queryKey: qk.settings, queryFn: () => api<ErpSettings>("/api/settings"), staleTime: Infinity })

export const useCategories = (includeInactive = false) =>
  useQuery({ queryKey: qk.categories(includeInactive), queryFn: () => api<Category[]>("/api/categories", { query: { includeInactive } }), staleTime: 60_000 })

export const useWarehouses = (includeInactive = false) =>
  useQuery({ queryKey: qk.warehouses(includeInactive), queryFn: () => api<Warehouse[]>("/api/warehouses", { query: { includeInactive } }), staleTime: 60_000 })

export const useDeliveryCompanies = (includeInactive = false) =>
  useQuery({ queryKey: qk.deliveryCompanies(includeInactive), queryFn: () => api<DeliveryCompany[]>("/api/delivery-companies", { query: { includeInactive } }) })

/** Sellable quantities (bundles computed from components). Disabled when there is nothing to ask about. */
export function useAvailability(productIds: string[], warehouseId?: string, enabled = true) {
  const ids = [...new Set(productIds)].filter(Boolean)
  return useQuery({
    queryKey: qk.availability(ids, warehouseId),
    queryFn: async () => {
      const rows = await api<Availability[]>("/api/inventory/availability", { query: { productIds: ids, warehouseId } })
      return Object.fromEntries(rows.map((r) => [r.productId, r.available])) as Record<string, number>
    },
    enabled: enabled && ids.length > 0,
    placeholderData: keepPreviousData,
  })
}
