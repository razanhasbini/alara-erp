using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GiftLinesAndRemoveSkus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_gift",
                schema: "sales",
                table: "order_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserve gift orders created by the short-lived standalone-gift workflow.
            migrationBuilder.Sql("""
                UPDATE sales.order_items AS item
                SET is_gift = TRUE, unit_price = 0, line_discount = 0, line_total = 0
                FROM sales.orders AS order_row
                WHERE item.order_id = order_row.id AND order_row.is_gift = TRUE;

                UPDATE sales.orders
                SET payment_status = CASE WHEN status IN ('Cancelled', 'Returned') THEN 'Void' ELSE 'Unpaid' END,
                    paid_at = NULL
                WHERE payment_status = 'NotRequired';
                """);

            migrationBuilder.DropIndex(
                name: "ix_products_sku",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_orders_is_gift",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "sku",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "is_gift",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "sku",
                schema: "sales",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "component_sku",
                schema: "sales",
                table: "order_item_components");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sku",
                schema: "catalog",
                table: "products",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_gift",
                schema: "sales",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "sku",
                schema: "sales",
                table: "order_items",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "component_sku",
                schema: "sales",
                table: "order_item_components",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE catalog.products SET sku = LEFT(slug, 60) || '-' || LEFT(id::text, 8);
                UPDATE sales.order_items SET sku = LEFT(product_name, 80);
                UPDATE sales.order_item_components SET component_sku = LEFT(component_name, 80);
                UPDATE sales.orders AS order_row
                SET is_gift = EXISTS (
                    SELECT 1 FROM sales.order_items AS item
                    WHERE item.order_id = order_row.id AND item.is_gift = TRUE
                ) AND NOT EXISTS (
                    SELECT 1 FROM sales.order_items AS item
                    WHERE item.order_id = order_row.id AND item.is_gift = FALSE
                );
                """);

            migrationBuilder.DropColumn(
                name: "is_gift",
                schema: "sales",
                table: "order_items");

            migrationBuilder.CreateIndex(
                name: "ix_products_sku",
                schema: "catalog",
                table: "products",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_is_gift",
                schema: "sales",
                table: "orders",
                column: "is_gift");
        }
    }
}
