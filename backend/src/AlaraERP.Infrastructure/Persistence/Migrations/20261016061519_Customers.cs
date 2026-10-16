using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Customers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                schema: "sales",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            // Preserve every existing order in customer analytics. Phone digits are the durable identity;
            // the latest order supplies the current profile while the order itself keeps its original snapshot.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT DISTINCT ON (normalized_phone)
                        normalized_phone,
                        recipient_name,
                        recipient_phone,
                        recipient_city,
                        recipient_address,
                        MIN(created_at) OVER (PARTITION BY normalized_phone) AS first_seen,
                        MAX(updated_at) OVER (PARTITION BY normalized_phone) AS last_seen
                    FROM (
                        SELECT phone_source.*,
                            CASE
                                WHEN raw_phone LIKE '00961%' THEN substr(raw_phone, 3)
                                WHEN raw_phone LIKE '961%' THEN raw_phone
                                WHEN raw_phone LIKE '0%' AND length(raw_phone) IN (8, 9) THEN '961' || substr(raw_phone, 2)
                                WHEN length(raw_phone) IN (7, 8) THEN '961' || raw_phone
                                ELSE raw_phone
                            END AS normalized_phone
                        FROM (
                            SELECT o.*, regexp_replace(o.recipient_phone, '[^0-9]', '', 'g') AS raw_phone
                            FROM sales.orders o
                        ) phone_source
                    ) source
                    WHERE normalized_phone <> ''
                    ORDER BY normalized_phone, updated_at DESC, id DESC
                )
                INSERT INTO sales.customers
                    (id, name, phone, normalized_phone, email, city, address, notes, is_active, created_at, updated_at)
                SELECT
                    (substr(md5(normalized_phone), 1, 8) || '-' || substr(md5(normalized_phone), 9, 4) || '-' ||
                     substr(md5(normalized_phone), 13, 4) || '-' || substr(md5(normalized_phone), 17, 4) || '-' ||
                     substr(md5(normalized_phone), 21, 12))::uuid,
                    recipient_name, recipient_phone, normalized_phone, NULL, recipient_city, recipient_address, NULL,
                    TRUE, first_seen, last_seen
                FROM ranked;

                UPDATE sales.orders o
                SET customer_id = c.id
                FROM sales.customers c
                WHERE c.normalized_phone = CASE
                    WHEN regexp_replace(o.recipient_phone, '[^0-9]', '', 'g') LIKE '00961%' THEN substr(regexp_replace(o.recipient_phone, '[^0-9]', '', 'g'), 3)
                    WHEN regexp_replace(o.recipient_phone, '[^0-9]', '', 'g') LIKE '961%' THEN regexp_replace(o.recipient_phone, '[^0-9]', '', 'g')
                    WHEN regexp_replace(o.recipient_phone, '[^0-9]', '', 'g') LIKE '0%' AND length(regexp_replace(o.recipient_phone, '[^0-9]', '', 'g')) IN (8, 9) THEN '961' || substr(regexp_replace(o.recipient_phone, '[^0-9]', '', 'g'), 2)
                    WHEN length(regexp_replace(o.recipient_phone, '[^0-9]', '', 'g')) IN (7, 8) THEN '961' || regexp_replace(o.recipient_phone, '[^0-9]', '', 'g')
                    ELSE regexp_replace(o.recipient_phone, '[^0-9]', '', 'g')
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id",
                schema: "sales",
                table: "orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_is_active",
                schema: "sales",
                table: "customers",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_customers_name",
                schema: "sales",
                table: "customers",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_customers_normalized_phone",
                schema: "sales",
                table: "customers",
                column: "normalized_phone",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_orders_customers_customer_id",
                schema: "sales",
                table: "orders",
                column: "customer_id",
                principalSchema: "sales",
                principalTable: "customers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_orders_customers_customer_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "ix_orders_customer_id",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "sales",
                table: "orders");
        }
    }
}
