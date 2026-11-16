using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftCo.Migrations
{
    /// <summary>
    /// Repairs allocation shares that were saved without going through OrderAllocation.
    ///
    /// Three of the four order save paths never wrote a share, so every new link kept the model
    /// default of 1. A multi-project order therefore charged its full cost to each project, and an
    /// order that lost a project kept the departed one's share, charging less than it cost.
    ///
    /// No schema change - this is data only. Orders whose shares already sum to 1 are left alone,
    /// so a hand-entered split is never overwritten.
    /// </summary>
    public partial class RepairOrderAllocationShares : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A sole link always takes the whole cost.
            migrationBuilder.Sql("""
                UPDATE "OrderProjects" op
                SET "AllocationShare" = 1.0
                WHERE op."AllocationShare" <> 1.0
                  AND (SELECT count(*) FROM "OrderProjects" x
                       WHERE x."SupplierOrderId" = op."SupplierOrderId") = 1;
                """);

            // Several links that do not sum to 1 are reset to an even split - the same fallback the
            // form uses when no split is stated, and the only defensible one here since the
            // intended split was never recorded. The remainder goes to the last link by project id
            // so three thirds come to exactly 1 rather than 0.999999.
            migrationBuilder.Sql("""
                WITH broken AS (
                    SELECT "SupplierOrderId"
                    FROM "OrderProjects"
                    GROUP BY "SupplierOrderId"
                    HAVING count(*) > 1
                       AND abs(sum("AllocationShare") - 1.0) > 0.000001
                ),
                ranked AS (
                    SELECT op."SupplierOrderId",
                           op."ProjectId",
                           count(*) OVER (PARTITION BY op."SupplierOrderId") AS n,
                           row_number() OVER (PARTITION BY op."SupplierOrderId"
                                              ORDER BY op."ProjectId") AS rn,
                           round(1.0 / count(*) OVER (PARTITION BY op."SupplierOrderId"), 6)
                               AS each_share
                    FROM "OrderProjects" op
                    JOIN broken b ON b."SupplierOrderId" = op."SupplierOrderId"
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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo. The shares this corrected were wrong, and the values they held
            // were a default rather than anything anyone chose.
        }
    }
}
