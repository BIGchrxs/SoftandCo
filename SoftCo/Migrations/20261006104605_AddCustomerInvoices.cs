using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoftCo.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "integer", nullable: false),
                    ClientReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NetTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    VatTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PricesEnteredInclusive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InternalNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IssuedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    IssuedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SyncStatus = table.Column<int>(type: "integer", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)

                    // The xmin concurrency token is NOT created here. EF scaffolded
                    //     xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                    // because the model maps a shadow property to it, but xmin is a PostgreSQL
                    // system column that already exists on every table. Creating it fails with
                    //     42701: column name "xmin" conflicts with a system column name
                    // The same line was removed from AddConcurrencyTokens for SupplierOrders and
                    // PaymentRequests. Do not "fix" this by restoring it.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerInvoices_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerInvoices_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerInvoiceId = table.Column<int>(type: "integer", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    UnitPriceExclVat = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    VatTreatment = table.Column<int>(type: "integer", nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    LineNetExclVat = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    LineVatAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    LineTotalInclVat = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerInvoiceLines_CustomerInvoices_CustomerInvoiceId",
                        column: x => x.CustomerInvoiceId,
                        principalTable: "CustomerInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_CustomerInvoiceId",
                table: "Approvals",
                column: "CustomerInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoiceLines_CustomerInvoiceId",
                table: "CustomerInvoiceLines",
                column: "CustomerInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_ClientId",
                table: "CustomerInvoices",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_DueDate",
                table: "CustomerInvoices",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_InvoiceNumber",
                table: "CustomerInvoices",
                column: "InvoiceNumber",
                unique: true,
                filter: "\"InvoiceNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_ProjectId",
                table: "CustomerInvoices",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_Status",
                table: "CustomerInvoices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerInvoices_SyncStatus",
                table: "CustomerInvoices",
                column: "SyncStatus");

            migrationBuilder.AddForeignKey(
                name: "FK_Approvals_CustomerInvoices_CustomerInvoiceId",
                table: "Approvals",
                column: "CustomerInvoiceId",
                principalTable: "CustomerInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Approvals_CustomerInvoices_CustomerInvoiceId",
                table: "Approvals");

            migrationBuilder.DropTable(
                name: "CustomerInvoiceLines");

            migrationBuilder.DropTable(
                name: "CustomerInvoices");

            migrationBuilder.DropIndex(
                name: "IX_Approvals_CustomerInvoiceId",
                table: "Approvals");
        }
    }
}
