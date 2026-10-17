/** Permission names (mirror of the backend's Permissions class). The API enforces them; the UI only adapts. */
export const P = {
  products: { view: "products.view", create: "products.create", update: "products.update", delete: "products.delete", pricing: "products.manage_pricing" },
  inventory: { view: "inventory.view", receive: "inventory.receive", adjust: "inventory.adjust", transfer: "inventory.transfer", thresholds: "inventory.manage_thresholds" },
  warehouses: { manage: "warehouses.manage" },
  orders: { view: "orders.view", create: "orders.create", update: "orders.update", cancel: "orders.cancel", delete: "orders.delete", payments: "orders.manage_payments" },
  expenses: { manage: "expenses.manage" },
  reports: { view: "reports.view" },
  costs: { view: "costs.view" },
  users: { view: "users.view", create: "users.create", update: "users.update", assignRoles: "users.assign_roles" },
} as const
