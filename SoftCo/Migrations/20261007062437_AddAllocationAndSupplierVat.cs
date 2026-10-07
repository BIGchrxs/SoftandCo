using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftCo.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationAndSupplierVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VatTreatment",
                table: "SupplierOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "AllocationShare",
                table: "OrderProjects",
                type: "numeric(9,6)",
                nullable: false,
                defaultValue: 0m);

            // Every existing link defaults to 0, which would make every order cost nothing. They
            // are backfilled as an even split: exactly right for the ~32 orders serving a single
            // project, and a defensible starting point for the 7 that serve several.
            //
            // The remainder goes to the last link rather than being lost. A third at six decimal
            // places is 0.333333, and three of those come to 0.999999 - without this, a slice of
            // every three-project order's cost would quietly drop out of gross profit, overstating
            // margin on exactly the orders hardest to check by hand.
            migrationBuilder.Sql("""
                WITH counts AS (
                    SELECT "SupplierOrderId", count(*) AS n
                    FROM "OrderProjects"
                    GROUP BY "SupplierOrderId"
                ),
                ranked AS (
                    SELECT op."SupplierOrderId",
                           op."ProjectId",
                           c.n,
                           row_number() OVER (PARTITION BY op."SupplierOrderId"
                                              ORDER BY op."ProjectId") AS rn,
                           round(1.0 / c.n, 6) AS each_share
                    FROM "OrderProjects" op
                    JOIN counts c ON c."SupplierOrderId" = op."SupplierOrderId"
                )
                UPDATE "OrderProjects" op
                SET "AllocationShare" = CASE
                        WHEN r.rn < r.n THEN r.each_share
                        ELSE 1.0 - r.each_share * (r.n - 1)
                    END
                FROM ranked r
                WHERE op."SupplierOrderId" = r."SupplierOrderId"
                  AND op."ProjectId" = r."ProjectId";
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderProjects_AllocationShare",
                table: "OrderProjects",
                sql: "\"AllocationShare\" >= 0 AND \"AllocationShare\" <= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderProjects_AllocationShare",
                table: "OrderProjects");

            migrationBuilder.DropColumn(
                name: "VatTreatment",
                table: "SupplierOrders");

            migrationBuilder.DropColumn(
                name: "AllocationShare",
                table: "OrderProjects");
        }
    }
}
