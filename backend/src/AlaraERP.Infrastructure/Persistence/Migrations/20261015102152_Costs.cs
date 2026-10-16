using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Costs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movements",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "standard_cost",
                schema: "catalog",
                table: "products",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "allocated_revenue",
                schema: "sales",
                table: "order_item_components",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "batches",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_standard_cost",
                schema: "catalog",
                table: "products",
                sql: "standard_cost IS NULL OR standard_cost >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_batches_unit_cost",
                schema: "inventory",
                table: "batches",
                sql: "unit_cost IS NULL OR unit_cost >= 0");

            // Receipt lines already stored invoice costs before batches and movements gained cost snapshots.
            // Preserve that history during an upgrade instead of making every existing sale look cost-free.
            migrationBuilder.Sql(
                """
                UPDATE inventory.batches AS b
                SET unit_cost = x.unit_cost
                FROM (
                    SELECT batch_id,
                           ROUND(SUM(quantity * unit_cost) / NULLIF(SUM(quantity), 0), 4) AS unit_cost
                    FROM inventory.stock_receipt_lines
                    WHERE unit_cost IS NOT NULL
                    GROUP BY batch_id
                ) AS x
                WHERE b.id = x.batch_id;

                UPDATE inventory.stock_movements AS m
                SET unit_cost = b.unit_cost
                FROM inventory.batches AS b
                WHERE m.batch_id = b.id AND m.unit_cost IS NULL;
                """);

            // Older order component snapshots did not store their share of bundle revenue. Current catalogue
            // prices are the best recoverable weights; quantity is the fallback for zero-price components.
            // A final remainder pass guarantees that component shares add up to the order line to the cent.
            migrationBuilder.Sql(
                """
                WITH component_weights AS (
                    SELECT c.id,
                           i.line_total,
                           p.selling_price * c.quantity_per_bundle AS price_weight,
                           c.quantity_per_bundle AS quantity_weight,
                           SUM(p.selling_price * c.quantity_per_bundle) OVER (PARTITION BY c.order_item_id) AS total_price_weight,
                           SUM(c.quantity_per_bundle) OVER (PARTITION BY c.order_item_id) AS total_quantity_weight
                    FROM sales.order_item_components AS c
                    JOIN sales.order_items AS i ON i.id = c.order_item_id
                    JOIN catalog.products AS p ON p.id = c.component_product_id
                )
                UPDATE sales.order_item_components AS c
                SET allocated_revenue = ROUND(
                    w.line_total *
                    CASE WHEN w.total_price_weight > 0 THEN w.price_weight ELSE w.quantity_weight END /
                    CASE WHEN w.total_price_weight > 0 THEN w.total_price_weight ELSE w.total_quantity_weight END,
                    2)
                FROM component_weights AS w
                WHERE c.id = w.id;

                WITH allocations AS (
                    SELECT c.id,
                           i.line_total,
                           SUM(c.allocated_revenue) OVER (PARTITION BY c.order_item_id) AS allocated_total,
                           ROW_NUMBER() OVER (PARTITION BY c.order_item_id ORDER BY c.id DESC) AS reverse_position
                    FROM sales.order_item_components AS c
                    JOIN sales.order_items AS i ON i.id = c.order_item_id
                )
                UPDATE sales.order_item_components AS c
                SET allocated_revenue = c.allocated_revenue + (a.line_total - a.allocated_total)
                FROM allocations AS a
                WHERE c.id = a.id AND a.reverse_position = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_products_standard_cost",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_batches_unit_cost",
                schema: "inventory",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "standard_cost",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "allocated_revenue",
                schema: "sales",
                table: "order_item_components");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "inventory",
                table: "batches");
        }
    }
}
