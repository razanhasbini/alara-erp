using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDeliveryAddBills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_expenses",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "delivery_companies",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_amounts_not_negative",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_grand_total",
                schema: "sales",
                table: "orders");

            // Delivery is no longer tracked: shipped/delivered orders are simply sold, and totals no longer include a fee.
            migrationBuilder.Sql("""
                UPDATE sales.orders SET status = 'Confirmed' WHERE status IN ('Shipped', 'Delivered');
                UPDATE sales.orders SET grand_total = subtotal - discount_total WHERE grand_total <> subtotal - discount_total;
                """);

            migrationBuilder.DropColumn(
                name: "delivered_at",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipped_at",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_total",
                schema: "sales",
                table: "orders");

            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "expenses",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    incurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expenses", x => x.id);
                    table.CheckConstraint("ck_expenses_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_expenses_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_amounts_not_negative",
                schema: "sales",
                table: "orders",
                sql: "subtotal >= 0 AND discount_total >= 0 AND grand_total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_grand_total",
                schema: "sales",
                table: "orders",
                sql: "grand_total = subtotal - discount_total");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_category_incurred_on",
                schema: "finance",
                table: "expenses",
                columns: new[] { "category", "incurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_incurred_on",
                schema: "finance",
                table: "expenses",
                column: "incurred_on");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_recorded_by_user_id",
                schema: "finance",
                table: "expenses",
                column: "recorded_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expenses",
                schema: "finance");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_amounts_not_negative",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_grand_total",
                schema: "sales",
                table: "orders");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at",
                schema: "sales",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shipped_at",
                schema: "sales",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "shipping_total",
                schema: "sales",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "delivery_companies",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_expenses",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivery_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_count = table.Column<int>(type: "integer", nullable: false),
                    incurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_expenses", x => x.id);
                    table.CheckConstraint("ck_delivery_expenses_amount", "amount > 0");
                    table.CheckConstraint("ck_delivery_expenses_count", "delivery_count > 0");
                    table.ForeignKey(
                        name: "fk_delivery_expenses_delivery_companies_delivery_company_id",
                        column: x => x.delivery_company_id,
                        principalSchema: "sales",
                        principalTable: "delivery_companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delivery_expenses_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_amounts_not_negative",
                schema: "sales",
                table: "orders",
                sql: "subtotal >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND grand_total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_grand_total",
                schema: "sales",
                table: "orders",
                sql: "grand_total = subtotal - discount_total + shipping_total");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_companies_name",
                schema: "sales",
                table: "delivery_companies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_expenses_delivery_company_id_incurred_on",
                schema: "sales",
                table: "delivery_expenses",
                columns: new[] { "delivery_company_id", "incurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_expenses_incurred_on",
                schema: "sales",
                table: "delivery_expenses",
                column: "incurred_on");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_expenses_recorded_by_user_id",
                schema: "sales",
                table: "delivery_expenses",
                column: "recorded_by_user_id");
        }
    }
}
