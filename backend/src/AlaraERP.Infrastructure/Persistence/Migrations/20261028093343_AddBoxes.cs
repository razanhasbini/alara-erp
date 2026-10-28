using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBoxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boxes",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    low_stock_alerted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boxes", x => x.id);
                    table.CheckConstraint("ck_boxes_on_hand", "quantity_on_hand >= 0");
                    table.CheckConstraint("ck_boxes_price", "price IS NULL OR price >= 0");
                    table.CheckConstraint("ck_boxes_threshold", "low_stock_threshold >= 0");
                });

            migrationBuilder.CreateTable(
                name: "box_contents",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    box_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_box_contents", x => x.id);
                    table.CheckConstraint("ck_box_contents_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_box_contents_boxes_box_id",
                        column: x => x.box_id,
                        principalSchema: "inventory",
                        principalTable: "boxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_box_contents_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "box_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    box_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity_change = table.Column<int>(type: "integer", nullable: false),
                    quantity_after = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_box_movements", x => x.id);
                    table.CheckConstraint("ck_box_movements_after", "quantity_after >= 0");
                    table.CheckConstraint("ck_box_movements_nonzero", "quantity_change <> 0");
                    table.ForeignKey(
                        name: "fk_box_movements_boxes_box_id",
                        column: x => x.box_id,
                        principalSchema: "inventory",
                        principalTable: "boxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_box_movements_users_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_box_contents_box_id_product_id",
                schema: "inventory",
                table: "box_contents",
                columns: new[] { "box_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_box_contents_product_id",
                schema: "inventory",
                table: "box_contents",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_box_movements_box_id_created_at",
                schema: "inventory",
                table: "box_movements",
                columns: new[] { "box_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_box_movements_performed_by_user_id",
                schema: "inventory",
                table: "box_movements",
                column: "performed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_boxes_name",
                schema: "inventory",
                table: "boxes",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "box_contents",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "box_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "boxes",
                schema: "inventory");
        }
    }
}
