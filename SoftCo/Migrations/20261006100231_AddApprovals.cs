using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoftCo.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PoApprovalStatus",
                table: "SupplierOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Approvals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SupplierOrderId = table.Column<int>(type: "integer", nullable: true),
                    PaymentRequestId = table.Column<int>(type: "integer", nullable: true),
                    CustomerInvoiceId = table.Column<int>(type: "integer", nullable: true),
                    AmountZarAtRequest = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SubjectFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RequestedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RequestNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    DecidedByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NotificationSent = table.Column<bool>(type: "boolean", nullable: false),
                    NotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotifiedTo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NotificationError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Approvals", x => x.Id);
                    table.CheckConstraint("CK_Approvals_OneSubject", "num_nonnulls(\"SupplierOrderId\", \"PaymentRequestId\", \"CustomerInvoiceId\") = 1");
                    table.ForeignKey(
                        name: "FK_Approvals_PaymentRequests_PaymentRequestId",
                        column: x => x.PaymentRequestId,
                        principalTable: "PaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Approvals_SupplierOrders_SupplierOrderId",
                        column: x => x.SupplierOrderId,
                        principalTable: "SupplierOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierOrders_PoApprovalStatus",
                table: "SupplierOrders",
                column: "PoApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_PaymentRequestId",
                table: "Approvals",
                column: "PaymentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_Status_Kind_RequestedAt",
                table: "Approvals",
                columns: new[] { "Status", "Kind", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_SupplierOrderId",
                table: "Approvals",
                column: "SupplierOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Approvals");

            migrationBuilder.DropIndex(
                name: "IX_SupplierOrders_PoApprovalStatus",
                table: "SupplierOrders");

            migrationBuilder.DropColumn(
                name: "PoApprovalStatus",
                table: "SupplierOrders");
        }
    }
}
