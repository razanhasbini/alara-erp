using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaidOnSaleAndDeliveryCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_orders_courier_remittances_courier_remittance_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "fk_orders_delivery_companies_delivery_company_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.CreateTable(
                name: "delivery_expenses",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    incurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    delivery_count = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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

            // Preserve historical delivery fees without retaining the old per-order payment workflow.
            // A hand-over is one expense entry; unhanded orders with a fee become one entry each.
            migrationBuilder.Sql(
                """
                INSERT INTO sales.delivery_expenses
                    (id, delivery_company_id, incurred_on, amount, delivery_count, notes,
                     recorded_by_user_id, created_at, updated_at)
                SELECT gen_random_uuid(), r.delivery_company_id,
                       (r.received_at AT TIME ZONE 'Asia/Beirut')::date,
                       r.courier_fees, r.order_count,
                       concat_ws(' · ', 'Imported delivery hand-over ' || r.remittance_number, r.notes),
                       r.recorded_by_user_id, r.created_at, r.created_at
                FROM sales.courier_remittances r
                WHERE r.courier_fees > 0;

                INSERT INTO sales.delivery_expenses
                    (id, delivery_company_id, incurred_on, amount, delivery_count, notes,
                     recorded_by_user_id, created_at, updated_at)
                SELECT gen_random_uuid(), o.delivery_company_id,
                       (COALESCE(o.shipped_at, o.confirmed_at, o.created_at) AT TIME ZONE 'Asia/Beirut')::date,
                       o.courier_fee, 1, 'Imported from order ' || o.order_number,
                       o.created_by_user_id, o.created_at, o.updated_at
                FROM sales.orders o
                WHERE o.delivery_company_id IS NOT NULL
                  AND o.courier_fee > 0
                  AND o.courier_remittance_id IS NULL;

                INSERT INTO sales.order_history
                    (id, order_id, event_type, old_value, new_value, notes, changed_by_user_id, changed_at)
                SELECT gen_random_uuid(), o.id, 'Payment', o.payment_status, 'Paid',
                       'Payment marked paid when migrating to paid-on-sale',
                       NULL, COALESCE(o.confirmed_at, o.created_at)
                FROM sales.orders o
                WHERE o.status IN ('Confirmed', 'Shipped', 'Delivered')
                  AND o.payment_status <> 'Paid';

                UPDATE sales.orders
                SET payment_status = 'Paid',
                    paid_at = COALESCE(paid_at, confirmed_at, created_at)
                WHERE status IN ('Confirmed', 'Shipped', 'Delivered');

                UPDATE sales.orders
                SET payment_status = 'Unpaid', paid_at = NULL
                WHERE status IN ('Draft', 'Reserved');

                UPDATE sales.orders
                SET payment_status = 'Void', paid_at = NULL
                WHERE status IN ('Cancelled', 'Returned');
                """);

            migrationBuilder.DropTable(
                name: "courier_remittances",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "ix_orders_courier_remittance_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_delivery_company_id_payment_status",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_amounts_not_negative",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "courier_fee",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "courier_remittance_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "delivery_company_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tracking_number",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropSequence(
                name: "remittance_number_seq",
                schema: "sales");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_amounts_not_negative",
                schema: "sales",
                table: "orders",
                sql: "subtotal >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND grand_total >= 0");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_expenses",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_amounts_not_negative",
                schema: "sales",
                table: "orders");

            migrationBuilder.CreateSequence(
                name: "remittance_number_seq",
                schema: "sales");

            migrationBuilder.AddColumn<decimal>(
                name: "courier_fee",
                schema: "sales",
                table: "orders",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "courier_remittance_id",
                schema: "sales",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "delivery_company_id",
                schema: "sales",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_number",
                schema: "sales",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "courier_remittances",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_collected = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    courier_fees = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivery_company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    order_count = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remittance_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_orders_courier_remittance_id",
                schema: "sales",
                table: "orders",
                column: "courier_remittance_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_delivery_company_id_payment_status",
                schema: "sales",
                table: "orders",
                columns: new[] { "delivery_company_id", "payment_status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_amounts_not_negative",
                schema: "sales",
                table: "orders",
                sql: "subtotal >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND grand_total >= 0 AND (courier_fee IS NULL OR courier_fee >= 0)");

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

            migrationBuilder.AddForeignKey(
                name: "fk_orders_courier_remittances_courier_remittance_id",
                schema: "sales",
                table: "orders",
                column: "courier_remittance_id",
                principalSchema: "sales",
                principalTable: "courier_remittances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_orders_delivery_companies_delivery_company_id",
                schema: "sales",
                table: "orders",
                column: "delivery_company_id",
                principalSchema: "sales",
                principalTable: "delivery_companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
