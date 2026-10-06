using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftCo.Migrations
{
    /// <summary>
    /// The sequences behind INV-2026-0001 and CN-2026-0001, created the same way po_number_seq was:
    /// raw SQL, because there is no HasSequence in the model and these are not EF-owned objects.
    ///
    /// Nothing issues invoices or credit notes yet. The sequences come first so that the code which
    /// will is never the thing that creates them - a generator that quietly created a missing
    /// sequence on first use would hide a failed migration until the numbers were already wrong.
    /// </summary>
    public partial class AddInvoiceAndCreditNoteSequences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Separate sequences per document kind, not one shared counter: an invoice and the
            // credit note that reverses it would otherwise be numbered from the same pool, and the
            // gaps in each series would be unexplainable to anyone reading the books.
            //
            // No backfill and no setval. Both tables are yet to exist, so starting at 1 is correct -
            // unlike po_number_seq, which had to be moved past 29 backfilled orders.
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS invoice_number_seq START 1;");
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS credit_note_number_seq START 1;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS invoice_number_seq;");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS credit_note_number_seq;");
        }
    }
}
