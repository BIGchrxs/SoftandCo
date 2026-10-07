using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftCo.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "InvoiceReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "InvoiceReceipts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "InvoiceReceipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SyncStatus",
                table: "InvoiceReceipts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceReceipts_SyncStatus",
                table: "InvoiceReceipts",
                column: "SyncStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceReceipts_SyncStatus",
                table: "InvoiceReceipts");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "InvoiceReceipts");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "InvoiceReceipts");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "InvoiceReceipts");

            migrationBuilder.DropColumn(
                name: "SyncStatus",
                table: "InvoiceReceipts");
        }
    }
}
