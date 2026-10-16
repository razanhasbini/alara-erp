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
  barcode: string | null
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
  lines: { productId: string; productName: string; batchId: string; batchNumber: string; expiryDate: string | null; quantity: number; unitCost: number | null }[]
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
export type OrderStatus = "Draft" | "Confirmed" | "Shipped" | "Delivered" | "Cancelled" | "Returned"
export type OrderSource = "WhatsApp" | "Instagram" | "Website" | "Other"
export type PaymentMethod = "CashOnDelivery" | "Whish" | "Omt" | "BankTransfer" | "Cash"
export type PaymentStatus = "Unpaid" | "CollectedByCourier" | "Paid" | "Void"

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
  deliveryCompanyName: string | null
  trackingNumber: string | null
  createdAt: string
  createdBy: string | null
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
  eventType: "Status" | "Payment"
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
  deliveryCompanyId: string | null
  deliveryCompanyName: string | null
  trackingNumber: string | null
  subtotal: number
  discountTotal: number
  shippingTotal: number
  courierFee: number | null
  grandTotal: number
  currencyCode: string
  courierRemittanceNumber: string | null
  notes: string | null
  createdByUserId: string | null
  createdBy: string | null
  createdAt: string
  updatedAt: string
  confirmedAt: string | null
  shippedAt: string | null
  deliveredAt: string | null
  cancelledAt: string | null
  returnedAt: string | null
  paidAt: string | null
  allowedTransitions: OrderStatus[]
  items: OrderItem[]
  history: OrderHistoryEntry[]
}

export interface DeliveryCompany {
  id: string
  name: string
  phone: string | null
  isActive: boolean
  awaitingRemittanceOrders: number
  awaitingRemittanceAmount: number
  awaitingRemittanceFees: number
}

export interface RemittanceOrder {
  id: string
  orderNumber: string
  recipientName: string
  deliveredAt: string | null
  grandTotal: number
  courierFee: number | null
}

export interface Remittance {
  id: string
  remittanceNumber: string
  deliveryCompanyId: string
  deliveryCompanyName: string
  amountCollected: number
  courierFees: number
  netAmount: number
  orderCount: number
  receivedAt: string
  notes: string | null
  recordedBy: string | null
  createdAt: string
  orders: RemittanceOrder[]
}

// ---- dashboard / settings -----------------------------------------------------------------
export interface Dashboard {
  generatedAt: string
  catalog: { activeProducts: number; activeBundles: number; inactiveProducts: number } | null
  orders: {
    total: number; drafts: number; awaitingShipment: number; outForDelivery: number
    delivered: number; cancelled: number; returned: number; createdToday: number
  } | null
  finance: {
    currency: string; collectedRevenue: number; collectedThisMonth: number; awaitingRemittance: number
    openOrderValue: number; courierFeesPaid: number
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
  defaultDeliveryFee: number
  expiryWarningDays: number
  expiryDatesEnabled: boolean
  enabledPaymentMethods: PaymentMethod[]
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
    deliveryFees: number; courierFees: number | null; deliveryResult: number | null
    costOfGoods: number | null; grossProfit: number | null; grossMarginPercent: number | null; profit: number | null
    cashReceived: number; uncostedUnits: number
  }
  series: { periodStart: string; periodEnd: string; orders: number; unitsSold: number; netSales: number; costOfGoods: number | null; grossProfit: number | null; profit: number | null; cashReceived: number }[]
  byChannel: { source: OrderSource; orders: number; netSales: number; grossProfit: number | null }[]
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
