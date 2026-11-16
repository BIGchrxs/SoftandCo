using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftCo.Migrations
{
    /// <summary>
    /// The sequence behind PRJ-2026-0020, created the same way the purchase-order, invoice and
    /// credit-note sequences were: raw SQL, because there is no HasSequence in the model and these
    /// are not EF-owned objects.
    ///
    /// Project codes were typed by hand until now and are all name-derived - FAIRMONT, DE-WET,
    /// RAVELLO-202--401. Those are left exactly as they are: they are meaningful, they are what the
    /// business already refers to, and rewriting nineteen live references to make a format
    /// consistent would be a cost with no benefit. Only new projects are numbered.
    /// </summary>
    public partial class AddProjectNumberSequence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS project_number_seq START 1;");

            // Moved past the projects that already exist, so the first generated code reads as the
            // twentieth project rather than the first. Nothing depends on this - the old codes are
            // not numeric and cannot collide - but a register whose numbering starts at 0001 beside
            // nineteen older rows invites the question of what happened to the first nineteen.
            migrationBuilder.Sql("""
                SELECT setval('project_number_seq',
                              GREATEST((SELECT COUNT(*) FROM "Projects"), 1));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS project_number_seq;");
        }
    }
}
