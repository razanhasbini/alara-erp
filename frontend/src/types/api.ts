/** Mirrors of the ASP.NET DTOs (enums are serialised as strings). */

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

// ---- identity -----------------------------------------------------------------------------
export interface CurrentUser {
  id: string
  email: string
  firstName: string
  lastName: string
  mustChangePassword: boolean
  roles: string[]
  permissions: string[]
}

export interface UserSummary {
  id: string
  email: string
  firstName: string
  lastName: string
  isActive: boolean
  isLockedOut: boolean
  roles: string[]
  createdAt: string
  lastLoginAt: string | null
}

export interface Role {
  name: string
  description: string
  isSystem: boolean
  permissions: string[]
  userCount: number
}

// ---- catalog ------------------------------------------------------------------------------
export type ProductType = "Individual" | "Bundle"
export type SizeUnit = "Ml" | "G" | "Pcs"

export interface Category {
  id: string
  name: string
  slug: string
  description: string | null
  parentCategoryId: string | null
  parentName: string | null
  sortOrder: number
  isActive: boolean
  productCount: number
  updatedAt: string
}

export interface ProductListItem {
  id: string
  name: string
  type: ProductType
  categoryId: string | null
  categoryName: string | null
  sellingPrice: number
  compareAtPrice: number | null
  isActive: boolean
  primaryImageId: string | null
  componentCount: number
  updatedAt: string
}

export interface BundleItem {
  componentProductId: string
  name: string
  quantity: number
  unitPrice: number
  isActive: boolean
}

export interface ProductImage {
  id: string
  contentType: string
  sizeBytes: number
  displayOrder: number
  isPrimary: boolean
}

export interface ProductDetail {
  id: string
  type: ProductType
  name: string
  slug: string
  categoryId: string | null
  categoryName: string | null
  shortDescription: string | null
  description: string | null
  howToUse: string | null
  ingredients: string | null
  sizeValue: number | null
  sizeUnit: SizeUnit | null
  weightGrams: number | null
  sellingPrice: number
  compareAtPrice: number | null
  /** Only present for employees with costs.view. */
  standardCost: number | null
  isActive: boolean
  bundleItems: BundleItem[]
  componentsValue: number | null
  images: ProductImage[]
  createdAt: string
  updatedAt: string
}

// ---- inventory ----------------------------------------------------------------------------
export interface Warehouse {
  id: string
  name: string
  code: string
  address: string | null
  isActive: boolean
  unitsOnHand: number
  unitsReserved: number
  productsInStock: number
  updatedAt: string
}

export type StockStatus = "InStock" | "Low" | "Out"
export type StockFilter = "All" | "Low" | "Out" | "Expiring" | "Expired"

export interface StockRow {
  productId: string
  name: string
  categoryName: string | null
  isActive: boolean
  onHand: number
  reserved: number
  available: number
  lowStockThreshold: number
  status: StockStatus
  nearestExpiry: string | null
  expiringUnits: number
  expiredUnits: number
  primaryImageId: string | null
}

export interface InventorySettings {
  productId: string
  lowStockThreshold: number
  tracksExpiry: boolean
  isDefault: boolean
  updatedAt: string | null
}

export interface ProductStock {
  productId: string
  name: string
  primaryImageId: string | null
  settings: InventorySettings
  onHand: number
  reserved: number
  available: number
  status: StockStatus
  warehouses: { warehouseId: string; name: string; code: string; onHand: number; reserved: number; available: number }[]
  batches: {
    batchId: string
    batchNumber: string
    expiryDate: string | null
    warehouseId: string
    warehouseCode: string
    onHand: number
    isExpired: boolean
    daysToExpiry: number | null
    unitCost: number | null
    /** When this delivery was added to stock. */
    receivedAt: string | null
  }[]
  /** Only present for employees with costs.view. */
  cost: {
    standardCost: number | null
    averageCost: number | null
    lastReceiptCost: number | null
    lastReceivedAt: string | null
    stockValue: number | null
    uncostedUnits: number
  } | null
}

export interface Availability {
  productId: string
  available: number
}

export type MovementType = "Receipt" | "Shipment" | "Return" | "AdjustmentIncrease" | "AdjustmentDecrease" | "TransferOut" | "TransferIn"
export type AdjustmentReason = "Damaged" | "Expired" | "CountCorrection" | "Lost" | "SampleOrGift" | "Other"
export type MovementReference = "Receipt" | "Transfer" | "Order" | "Adjustment"

export interface Movement {
  id: string
  createdAt: string
  warehouseId: string
  warehouseCode: string
  productId: string
  productName: string
  batchNumber: string
  expiryDate: string | null
  type: MovementType
  quantityChange: number
  reason: AdjustmentReason | null
  referenceType: MovementReference
  referenceId: string
  referenceNumber: string | null
  notes: string | null
  performedBy: string | null
  unitCost: number | null
}

export interface ReceiptSummary {
  id: string
  receiptNumber: string
  warehouseCode: string
  supplierName: string | null
  supplierReference: string | null
  receivedAt: string
  lineCount: number
  totalUnits: number
  performedBy: string | null
}

export interface Receipt {
  id: string
  receiptNumber: string
  warehouseId: string
  warehouseName: string
  supplierName: string | null
  supplierReference: string | null
  receivedAt: string
  notes: string | null
  performedBy: string | null
  createdAt: string
  totalUnits: number
  totalCost: number | null
  lines: { productId: string; productName: string; batchId: string; batchNumber: string; expiryDate: string | null; quantity: number; unitCost: number | null; costFromSaved: boolean }[]
}

export interface Transfer {
  id: string
  transferNumber: string
  fromWarehouseId: string
  fromWarehouse: string
  toWarehouseId: string
  toWarehouse: string
  status: "Completed"
  notes: string | null
  performedBy: string | null
  createdAt: string
  totalUnits: number
  lines: { productId: string; productName: string; batchId: string; batchNumber: string; expiryDate: string | null; quantity: number }[]
}

// ---- sales --------------------------------------------------------------------------------
export type OrderStatus = "Draft" | "Reserved" | "Confirmed" | "Cancelled" | "Returned"
export type OrderSource = "WhatsApp" | "Instagram" | "Website" | "Other"
export type PaymentMethod = "CashOnDelivery" | "Whish" | "Omt" | "BankTransfer" | "Cash"
/** Reserved orders are Unpaid; an order is Paid the moment it is sold; cancelled or returned sales are Void. */
export type PaymentStatus = "Unpaid" | "Paid" | "Void"

export interface Customer {
  id: string
  name: string
  phone: string
  email: string | null
  city: string | null
  address: string | null
  notes: string | null
  isActive: boolean
  orderCount: number
  totalSpent: number
  totalPaid: number
  averageOrderValue: number
  firstOrderAt: string | null
  lastOrderAt: string | null
  createdAt: string
  updatedAt: string
}

export interface OrderListItem {
  id: string
  orderNumber: string
  status: OrderStatus
  paymentStatus: PaymentStatus
  paymentMethod: PaymentMethod
  source: OrderSource
  recipientName: string
  recipientPhone: string
  recipientCity: string | null
  itemCount: number
  grandTotal: number
  currencyCode: string
  createdAt: string
  createdBy: string | null
  reservedUntil: string | null
}

export interface OrderItem {
  id: string
  productId: string
  productName: string
  productType: ProductType
  isGift: boolean
  quantity: number
  unitPrice: number
  lineDiscount: number
  lineTotal: number
  components: { productId: string; name: string; quantityPerBundle: number; totalQuantity: number }[]
}

export interface OrderHistoryEntry {
  eventType: "Status" | "Payment" | "Reservation"
  oldValue: string | null
  newValue: string
  notes: string | null
  changedAt: string
  changedBy: string | null
}

export interface OrderDetail {
  id: string
  orderNumber: string
  status: OrderStatus
  paymentStatus: PaymentStatus
  paymentMethod: PaymentMethod
  source: OrderSource
  warehouseId: string
  warehouseName: string
  customerId: string | null
  recipientName: string
  recipientPhone: string
  recipientCity: string | null
  recipientAddress: string | null
  subtotal: number
  discountTotal: number
  grandTotal: number
  currencyCode: string
  notes: string | null
  createdByUserId: string | null
  createdBy: string | null
  createdAt: string
  updatedAt: string
  confirmedAt: string | null
  cancelledAt: string | null
  returnedAt: string | null
  paidAt: string | null
  allowedTransitions: OrderStatus[]
  items: OrderItem[]
  history: OrderHistoryEntry[]
  reservedAt: string | null
  reservedUntil: string | null
}

// ---- bills (finance) ------------------------------------------------------------------------
export type ExpenseCategory = "Electricity" | "Internet" | "Models" | "Wages" | "Other"

export interface Expense {
  id: string
  category: ExpenseCategory
  /** yyyy-MM-dd: the date the cost applies to. */
  incurredOn: string
  amount: number
  description: string | null
  notes: string | null
  recordedBy: string | null
  createdAt: string
  updatedAt: string
}

export interface ExpenseList {
  from: string | null
  to: string | null
  total: number
  byCategory: { category: ExpenseCategory; count: number; amount: number }[]
  items: Expense[]
  page: number
  pageSize: number
  totalCount: number
}

// ---- dashboard / settings -----------------------------------------------------------------
export interface Dashboard {
  generatedAt: string
  catalog: { activeProducts: number; activeBundles: number; inactiveProducts: number } | null
  orders: {
    total: number; drafts: number; sold: number; reserved: number; cancelled: number; returned: number
    createdToday: number; soldToday: number
  } | null
  finance: {
    currency: string; collectedRevenue: number; collectedThisMonth: number
    /** Order totals of reserved orders: sales waiting for the customer. */
    reservedValue: number
    /** Bills dated this month; null unless the user can see costs. */
    billsThisMonth: number | null
  } | null
  dailyOrders: { date: string; orders: number; value: number }[] | null
  bySource: { source: OrderSource; orders: number; value: number }[] | null
  recentOrders: { id: string; orderNumber: string; status: OrderStatus; paymentStatus: PaymentStatus; recipientName: string; grandTotal: number; createdAt: string }[] | null
  inventory: {
    unitsOnHand: number; unitsReserved: number; lowStockCount: number; outOfStockCount: number
    expiredUnits: number; expiringUnits: number; expiryWarningDays: number
    byWarehouse: { warehouseId: string; code: string; name: string; onHand: number; reserved: number }[]
    lowStock: { productId: string; name: string; available: number; threshold: number }[]
    expiring: { productId: string; name: string; batchNumber: string; expiryDate: string; warehouseCode: string; units: number }[]
  } | null
  recentMovements: Movement[] | null
}

export interface ErpSettings {
  baseCurrency: string
  timeZone: string
  expiryWarningDays: number
  expiryDatesEnabled: boolean
}

export interface FeatureSettings {
  expiryDatesEnabled: boolean
  updatedAt: string | null
}

// ---- analytics -----------------------------------------------------------------------------
export type Grouping = "Day" | "Week" | "Month"

export interface AnalyticsSummary {
  from: string
  to: string
  groupBy: Grouping
  currency: string
  includesCosts: boolean
  totals: {
    orders: number; unitsSold: number; grossSales: number; discounts: number; netSales: number; averageOrderValue: number
    costOfGoods: number | null; grossProfit: number | null; grossMarginPercent: number | null
    /** Bills in the range; profit = gross profit − expenses (net profit). */
    expenses: number | null; profit: number | null; netMarginPercent: number | null
    cashReceived: number; uncostedUnits: number
  }
  series: { periodStart: string; periodEnd: string; orders: number; unitsSold: number; netSales: number; costOfGoods: number | null; grossProfit: number | null; expenses: number | null; profit: number | null; cashReceived: number }[]
  byChannel: { source: OrderSource; orders: number; netSales: number; grossProfit: number | null }[]
  expensesByCategory: { category: ExpenseCategory; amount: number }[] | null
}

export interface ProductProfit {
  productId: string; name: string; categoryName: string | null; isActive: boolean
  sellingPrice: number; standardCost: number | null; averageCost: number | null; unitMargin: number | null; unitMarginPercent: number | null
  unitsSold: number; unitsSoldInBundles: number; netSales: number; costOfGoods: number | null; grossProfit: number | null
  grossMarginPercent: number | null; uncostedUnits: number
}

export interface BundleProfit {
  productId: string; name: string; isActive: boolean; sellingPrice: number
  componentsCost: number | null; unitMargin: number | null; unitMarginPercent: number | null
  setsSold: number; netSales: number; costOfGoods: number | null; grossProfit: number | null; grossMarginPercent: number | null
}

export interface ProductAnalytics {
  from: string; to: string; currency: string; includesCosts: boolean
  products: ProductProfit[]
  bundles: BundleProfit[]
}

export interface CustomerLoyalty {
  customerId: string
  name: string
  phone: string
  city: string | null
  orderCount: number
  repeatOrders: number
  totalOrdered: number
  totalPaid: number
  averageOrderValue: number
  averageDaysBetweenOrders: number | null
  firstOrderAt: string
  lastOrderAt: string
}

export interface CustomerAnalytics {
  from: string
  to: string
  currency: string
  customers: CustomerLoyalty[]
}

// ---- reservations & alerts ------------------------------------------------------------------
export interface ReservedStock {
  productId: string
  reservedQuantity: number
  orders: { orderId: string; orderNumber: string; recipientName: string; quantity: number; reservedUntil: string | null }[]
}

export type WhatsAppStatus = "Pending" | "Sent" | "Failed" | "NotConfigured"

export interface AlertItem {
  id: string
  type: string
  title: string
  body: string
  link: string | null
  createdAt: string
  isRead: boolean
  whatsApp: { id: string; toPhone: string; status: WhatsAppStatus; attempts: number; lastError: string | null; sentAt: string | null }[]
}

export interface AlertChannels {
  whatsAppConfigured: boolean
  whatsAppRecipients: number
  usesTemplate: boolean
  whatsAppPending: number
  whatsAppNotSent: number
}
