using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoftCo.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReleaseApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "SentAt",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<bool>(
                name: "AttachInvoice",
                table: "PaymentRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RaisedAt",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "RaisedById",
                table: "PaymentRequests",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RaisedByName",
                table: "PaymentRequests",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseStatus",
                table: "PaymentRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Requests that predate this workflow were genuinely sent, so they are set explicitly
            // rather than left on the defaults. Without this they would read as "Awaiting FD
            // approval", raised in the year 1 - a request nobody raised, waiting on a decision
            // nobody can make, for an email that already went.
            migrationBuilder.Sql("""
                UPDATE "PaymentRequests"
                SET "ReleaseStatus" = 3,          -- Released
                    "AttachInvoice" = true,       -- what the old direct-send flow did by default
                    "RaisedAt"      = "SentAt",
                    "RaisedById"    = "SentById",
                    "RaisedByName"  = "SentByName";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SentAt goes back to NOT NULL below, so anything never sent has no honest value to
            // put there. Those rows are the ones this migration introduced the ability to create.
            migrationBuilder.Sql("""
                DELETE FROM "Approvals" WHERE "PaymentRequestId" IS NOT NULL;
                DELETE FROM "PaymentRequests" WHERE "SentAt" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "AttachInvoice",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "RaisedAt",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "RaisedById",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "RaisedByName",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "ReleaseStatus",
                table: "PaymentRequests");

            migrationBuilder.AlterColumn<DateTime>(
                name: "SentAt",
                table: "PaymentRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
