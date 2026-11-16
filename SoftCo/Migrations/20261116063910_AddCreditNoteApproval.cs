using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftCo.Migrations
{
    /// <summary>
    /// A fourth approval subject: the credit note. Until now anyone with edit rights could credit
    /// an issued invoice to nil without the Financial Director seeing it.
    ///
    /// The check constraint has to be dropped before the column is added and rebuilt after, because
    /// num_nonnulls names its columns. Existing rows satisfy the new version unchanged - they each
    /// have one of the other three subjects set and CreditNoteId null.
    /// </summary>
    public partial class AddCreditNoteApproval : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Approvals_OneSubject",
                table: "Approvals");

            migrationBuilder.AddColumn<int>(
                name: "CreditNoteId",
                table: "Approvals",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_CreditNoteId",
                table: "Approvals",
                column: "CreditNoteId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Approvals_OneSubject",
                table: "Approvals",
                sql: "num_nonnulls(\"SupplierOrderId\", \"PaymentRequestId\", \"CustomerInvoiceId\", \"CreditNoteId\") = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_Approvals_CreditNotes_CreditNoteId",
                table: "Approvals",
                column: "CreditNoteId",
                principalTable: "CreditNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Approvals_CreditNotes_CreditNoteId",
                table: "Approvals");

            migrationBuilder.DropIndex(
                name: "IX_Approvals_CreditNoteId",
                table: "Approvals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Approvals_OneSubject",
                table: "Approvals");

            migrationBuilder.DropColumn(
                name: "CreditNoteId",
                table: "Approvals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Approvals_OneSubject",
                table: "Approvals",
                sql: "num_nonnulls(\"SupplierOrderId\", \"PaymentRequestId\", \"CustomerInvoiceId\") = 1");
        }
    }
}
