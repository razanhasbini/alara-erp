using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlaraERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ControlledTestOrderDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE PROCEDURE sales.erase_test_order_ledger(IN p_order_id uuid)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = pg_catalog
                AS $procedure$
                BEGIN
                    DELETE FROM inventory.stock_movements
                    WHERE reference_type = 'Order' AND reference_id = p_order_id;

                    DELETE FROM sales.order_history
                    WHERE order_id = p_order_id;
                END;
                $procedure$;

                REVOKE ALL ON PROCEDURE sales.erase_test_order_ledger(uuid) FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS sales.erase_test_order_ledger(uuid);");
        }
    }
}
