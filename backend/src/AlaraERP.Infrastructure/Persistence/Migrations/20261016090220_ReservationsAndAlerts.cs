using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservationsAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_barcode",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "barcode",
                schema: "catalog",
                table: "products");

            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reservation_expiry_notified_at",
                schema: "sales",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reserved_at",
                schema: "sales",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reserved_until",
                schema: "sales",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    required_permission = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_reads",
                schema: "notifications",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_reads", x => new { x.notification_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_notification_reads_notifications_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notifications",
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notification_reads_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_messages",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_messages", x => x.id);
                    table.CheckConstraint("ck_whatsapp_messages_attempts", "attempts >= 0");
                    table.ForeignKey(
                        name: "fk_whatsapp_messages_notifications_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notifications",
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_open_reservations",
                schema: "sales",
                table: "orders",
                columns: new[] { "status", "reserved_until" },
                filter: "status = 'Reserved'");

            migrationBuilder.CreateIndex(
                name: "ix_notification_reads_user_id",
                schema: "notifications",
                table: "notification_reads",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_created_at",
                schema: "notifications",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_reference_type_reference_id",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_messages_notification_id",
                schema: "notifications",
                table: "whatsapp_messages",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_messages_status_created_at",
                schema: "notifications",
                table: "whatsapp_messages",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_reads",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "whatsapp_messages",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_orders_open_reservations",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "reservation_expiry_notified_at",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "reserved_at",
                schema: "sales",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "reserved_until",
                schema: "sales",
                table: "orders");

            migrationBuilder.AddColumn<string>(
                name: "barcode",
                schema: "catalog",
                table: "products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_barcode",
                schema: "catalog",
                table: "products",
                column: "barcode",
                unique: true);
        }
    }
}
