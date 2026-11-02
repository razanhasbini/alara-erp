using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppDeliveryReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                schema: "notifications",
                table: "whatsapp_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_messages_provider_message_id",
                schema: "notifications",
                table: "whatsapp_messages",
                column: "provider_message_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_whatsapp_messages_provider_message_id",
                schema: "notifications",
                table: "whatsapp_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                schema: "notifications",
                table: "whatsapp_messages");
        }
    }
}
