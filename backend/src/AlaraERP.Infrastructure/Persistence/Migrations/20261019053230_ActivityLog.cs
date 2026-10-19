using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "activity_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    area = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    link = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_log_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_area_at",
                schema: "audit",
                table: "activity_log",
                columns: new[] { "area", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_at",
                schema: "audit",
                table: "activity_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_user_id_at",
                schema: "audit",
                table: "activity_log",
                columns: new[] { "user_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_log",
                schema: "audit");
        }
    }
}
