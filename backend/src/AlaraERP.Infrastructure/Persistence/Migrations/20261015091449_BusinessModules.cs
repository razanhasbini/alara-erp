using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BusinessModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.CreateSequence(
                name: "order_number_seq",
                schema: "sales");

            migrationBuilder.CreateSequence(
                name: "receipt_number_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "remittance_number_seq",
                schema: "sales");

            migrationBuilder.CreateSequence(
                name: "transfer_number_seq",
                schema: "inventory");

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    parent_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.CheckConstraint("ck_categories_not_self_parent", "parent_category_id IS NULL OR parent_category_id <> id");
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_category_id",
                        column: x => x.parent_category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_companies",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    short_description = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    how_to_use = table.Column<string>(type: "text", nullable: true),
                    ingredients = table.Column<string>(type: "text", nullable: true),
                    size_value = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    size_unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    weight_grams = table.Column<int>(type: "integer", nullable: true),
                    selling_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    compare_at_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_compare_at_price", "compare_at_price IS NULL OR compare_at_price >= selling_price");
                    table.CheckConstraint("ck_products_selling_price", "selling_price >= 0");
                    table.CheckConstraint("ck_products_size", "(size_value IS NULL) = (size_unit IS NULL) AND (size_value IS NULL OR size_value > 0)");
                    table.CheckConstraint("ck_products_type", "type IN ('Individual', 'Bundle')");
                    table.CheckConstraint("ck_products_weight", "weight_grams IS NULL OR weight_grams >= 0");
                    table.ForeignKey(
                        name: "fk_products_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "courier_remittances",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    remittance_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    delivery_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_collected = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    courier_fees = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    order_count = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_courier_remittances", x => x.id);
                    table.CheckConstraint("ck_courier_remittances_amounts", "amount_collected >= 0 AND courier_fees >= 0 AND order_count > 0");
                    table.CheckConstraint("ck_courier_remittances_net", "net_amount = amount_collected - courier_fees");
                    table.ForeignKey(
                        name: "fk_courier_remittances_delivery_companies_delivery_company_id",
                        column: x => x.delivery_company_id,
                        principalSchema: "sales",
                        principalTable: "delivery_companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_courier_remittances_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipts",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    supplier_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_receipts_users_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_receipts_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    from_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfers", x => x.id);
                    table.CheckConstraint("ck_stock_transfers_distinct_warehouses", "from_warehouse_id <> to_warehouse_id");
                    table.ForeignKey(
                        name: "fk_stock_transfers_users_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_warehouses_from_warehouse_id",
                        column: x => x.from_warehouse_id,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfers_warehouses_to_warehouse_id",
                        column: x => x.to_warehouse_id,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batches",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_batches_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bundle_items",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bundle_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bundle_items", x => x.id);
                    table.CheckConstraint("ck_bundle_items_not_self", "bundle_product_id <> component_product_id");
                    table.CheckConstraint("ck_bundle_items_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_bundle_items_products_bundle_product_id",
                        column: x => x.bundle_product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bundle_items_products_component_product_id",
                        column: x => x.component_product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_images",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_images", x => x.id);
                    table.CheckConstraint("ck_product_images_size", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_product_images_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_settings",
                schema: "inventory",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: false),
                    tracks_expiry = table.Column<bool>(type: "boolean", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_settings", x => x.product_id);
                    table.CheckConstraint("ck_product_settings_threshold", "low_stock_threshold >= 0");
                    table.ForeignKey(
                        name: "fk_product_settings_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_product_settings_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                schema: "inventory",
                columns: table => new
                {
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    quantity_reserved = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_balances", x => new { x.warehouse_id, x.product_id });
                    table.CheckConstraint("ck_stock_balances_on_hand", "quantity_on_hand >= 0");
                    table.CheckConstraint("ck_stock_balances_reserved", "quantity_reserved >= 0 AND quantity_reserved <= quantity_on_hand");
                    table.ForeignKey(
                        name: "fk_stock_balances_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_balances_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    recipient_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    recipient_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recipient_address = table.Column<string>(type: "text", nullable: true),
                    delivery_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    shipping_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    courier_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    grand_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    courier_remittance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_amounts_not_negative", "subtotal >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND grand_total >= 0 AND (courier_fee IS NULL OR courier_fee >= 0)");
                    table.CheckConstraint("ck_orders_discount_within_subtotal", "discount_total <= subtotal");
                    table.CheckConstraint("ck_orders_grand_total", "grand_total = subtotal - discount_total + shipping_total");
                    table.CheckConstraint("ck_orders_paid_at", "(payment_status = 'Paid') = (paid_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_orders_courier_remittances_courier_remittance_id",
                        column: x => x.courier_remittance_id,
                        principalSchema: "sales",
                        principalTable: "courier_remittances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_delivery_companies_delivery_company_id",
                        column: x => x.delivery_company_id,
                        principalSchema: "sales",
                        principalTable: "delivery_companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_orders_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batch_balances",
                schema: "inventory",
                columns: table => new
                {
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_balances", x => new { x.warehouse_id, x.batch_id });
                    table.CheckConstraint("ck_batch_balances_on_hand", "quantity_on_hand >= 0");
                    table.ForeignKey(
                        name: "fk_batch_balances_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "inventory",
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_balances_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity_change = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    reference_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_nonzero", "quantity_change <> 0");
                    table.CheckConstraint("ck_stock_movements_sign", "(type IN ('Receipt', 'Return', 'AdjustmentIncrease', 'TransferIn')) = (quantity_change > 0)");
                    table.ForeignKey(
                        name: "fk_stock_movements_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "inventory",
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_users_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_receipt_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_receipt_lines", x => x.id);
                    table.CheckConstraint("ck_stock_receipt_lines_quantity", "quantity > 0");
                    table.CheckConstraint("ck_stock_receipt_lines_unit_cost", "unit_cost IS NULL OR unit_cost >= 0");
                    table.ForeignKey(
                        name: "fk_stock_receipt_lines_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "inventory",
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_receipt_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_receipt_lines_stock_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "inventory",
                        principalTable: "stock_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_lines", x => x.id);
                    table.CheckConstraint("ck_stock_transfer_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "inventory",
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_lines_stock_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalSchema: "inventory",
                        principalTable: "stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_history",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    old_value = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    new_value = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_history_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_history_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    product_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    line_discount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.CheckConstraint("ck_order_items_amounts", "unit_price >= 0 AND line_discount >= 0 AND line_total >= 0");
                    table.CheckConstraint("ck_order_items_line_total", "line_total = unit_price * quantity - line_discount");
                    table.CheckConstraint("ck_order_items_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_items_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.CheckConstraint("ck_stock_reservations_closed", "(status = 'Active') = (closed_at IS NULL)");
                    table.CheckConstraint("ck_stock_reservations_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_reservations_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "sales",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalSchema: "inventory",
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_item_components",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    component_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    component_sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    quantity_per_bundle = table.Column<int>(type: "integer", nullable: false),
                    total_quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_item_components", x => x.id);
                    table.CheckConstraint("ck_order_item_components_quantities", "quantity_per_bundle > 0 AND total_quantity > 0");
                    table.ForeignKey(
                        name: "fk_order_item_components_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalSchema: "sales",
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_order_item_components_products_component_product_id",
                        column: x => x.component_product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batch_balances_batch_id",
                schema: "inventory",
                table: "batch_balances",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_expiry_date",
                schema: "inventory",
                table: "batches",
                column: "expiry_date");

            migrationBuilder.CreateIndex(
                name: "ix_batches_product_id_batch_number",
                schema: "inventory",
                table: "batches",
                columns: new[] { "product_id", "batch_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bundle_items_bundle_product_id_component_product_id",
                schema: "catalog",
                table: "bundle_items",
                columns: new[] { "bundle_product_id", "component_product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bundle_items_component_product_id",
                schema: "catalog",
                table: "bundle_items",
                column: "component_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_category_id",
                schema: "catalog",
                table: "categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_slug",
                schema: "catalog",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_courier_remittances_delivery_company_id_received_at",
                schema: "sales",
                table: "courier_remittances",
                columns: new[] { "delivery_company_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_courier_remittances_recorded_by_user_id",
                schema: "sales",
                table: "courier_remittances",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_courier_remittances_remittance_number",
                schema: "sales",
                table: "courier_remittances",
                column: "remittance_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_companies_name",
                schema: "sales",
                table: "delivery_companies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_history_changed_by_user_id",
                schema: "sales",
                table: "order_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_history_order_id_changed_at",
                schema: "sales",
                table: "order_history",
                columns: new[] { "order_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_item_components_component_product_id",
                schema: "sales",
                table: "order_item_components",
                column: "component_product_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_item_components_order_item_id",
                schema: "sales",
                table: "order_item_components",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                schema: "sales",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_product_id",
                schema: "sales",
                table: "order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_courier_remittance_id",
                schema: "sales",
                table: "orders",
                column: "courier_remittance_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_created_at",
                schema: "sales",
                table: "orders",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_orders_created_by_user_id",
                schema: "sales",
                table: "orders",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_delivery_company_id_payment_status",
                schema: "sales",
                table: "orders",
                columns: new[] { "delivery_company_id", "payment_status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_order_number",
                schema: "sales",
                table: "orders",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_recipient_phone",
                schema: "sales",
                table: "orders",
                column: "recipient_phone");

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_created_at",
                schema: "sales",
                table: "orders",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_warehouse_id",
                schema: "sales",
                table: "orders",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_images_product_id_display_order",
                schema: "catalog",
                table: "product_images",
                columns: new[] { "product_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_product_images_storage_key",
                schema: "catalog",
                table: "product_images",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_product_images_one_primary",
                schema: "catalog",
                table: "product_images",
                column: "product_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_product_settings_updated_by_user_id",
                schema: "inventory",
                table: "product_settings",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_barcode",
                schema: "catalog",
                table: "products",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_category_id",
                schema: "catalog",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_is_active_name",
                schema: "catalog",
                table: "products",
                columns: new[] { "is_active", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_products_sku",
                schema: "catalog",
                table: "products",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_slug",
                schema: "catalog",
                table: "products",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_balances_product_id",
                schema: "inventory",
                table: "stock_balances",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_batch_id",
                schema: "inventory",
                table: "stock_movements",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_created_at",
                schema: "inventory",
                table: "stock_movements",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_performed_by_user_id",
                schema: "inventory",
                table: "stock_movements",
                column: "performed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_product_id_created_at",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "product_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_reference_type_reference_id",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_warehouse_id_created_at",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "warehouse_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipt_lines_batch_id",
                schema: "inventory",
                table: "stock_receipt_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipt_lines_product_id",
                schema: "inventory",
                table: "stock_receipt_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipt_lines_receipt_id",
                schema: "inventory",
                table: "stock_receipt_lines",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipts_performed_by_user_id",
                schema: "inventory",
                table: "stock_receipts",
                column: "performed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipts_receipt_number",
                schema: "inventory",
                table: "stock_receipts",
                column: "receipt_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipts_received_at",
                schema: "inventory",
                table: "stock_receipts",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_stock_receipts_warehouse_id",
                schema: "inventory",
                table: "stock_receipts",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_order_id_status",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "order_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_product_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_warehouse_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_one_active_per_product",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "order_id", "product_id" },
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_batch_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_product_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_lines_transfer_id",
                schema: "inventory",
                table: "stock_transfer_lines",
                column: "transfer_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_created_at",
                schema: "inventory",
                table: "stock_transfers",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_from_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                column: "from_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_performed_by_user_id",
                schema: "inventory",
                table: "stock_transfers",
                column: "performed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_to_warehouse_id",
                schema: "inventory",
                table: "stock_transfers",
                column: "to_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_transfer_number",
                schema: "inventory",
                table: "stock_transfers",
                column: "transfer_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_code",
                schema: "inventory",
                table: "warehouses",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batch_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "bundle_items",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "order_history",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "order_item_components",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "product_images",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_settings",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_receipt_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfer_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "order_items",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "stock_receipts",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "batches",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transfers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "courier_remittances",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "delivery_companies",
                schema: "sales");

            migrationBuilder.DropSequence(
                name: "order_number_seq",
                schema: "sales");

            migrationBuilder.DropSequence(
                name: "receipt_number_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "remittance_number_seq",
                schema: "sales");

            migrationBuilder.DropSequence(
                name: "transfer_number_seq",
                schema: "inventory");
        }
    }
}
