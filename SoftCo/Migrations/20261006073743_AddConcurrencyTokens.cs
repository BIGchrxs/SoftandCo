using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftCo.Migrations
{
    /// <summary>
    /// Deliberately empty. SupplierOrder and PaymentRequest now map a shadow property to
    /// PostgreSQL's <c>xmin</c> system column as an optimistic-concurrency token, so the model
    /// changed but the database did not: every table already has <c>xmin</c>.
    ///
    /// EF cannot know that and scaffolded AddColumn/DropColumn for it. Running that would fail
    /// with <c>42701: column name "xmin" conflicts with a system column name</c>, and the Down
    /// would try to drop a column PostgreSQL owns. Both bodies are therefore empty; the migration
    /// exists only so the model snapshot is recorded and later migrations do not re-emit this.
    ///
    /// Do not "fix" this by restoring the generated calls.
    /// </summary>
    public partial class AddConcurrencyTokens : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally no operations - see the class summary.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally no operations - see the class summary.
        }
    }
}
