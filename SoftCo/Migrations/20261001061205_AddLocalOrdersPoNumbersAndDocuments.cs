using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoftCo.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalOrdersPoNumbersAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderType",
                table: "SupplierOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PoNumber",
                table: "SupplierOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplierOrderId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    StoredName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderDocuments_SupplierOrders_SupplierOrderId",
                        column: x => x.SupplierOrderId,
                        principalTable: "SupplierOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RoleNote = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentContacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplierOrderId = table.Column<int>(type: "integer", nullable: false),
                    PaymentContactId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AmountZarAtRequest = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentById = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    SentByName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRequests_PaymentContacts_PaymentContactId",
                        column: x => x.PaymentContactId,
                        principalTable: "PaymentContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRequests_SupplierOrders_SupplierOrderId",
                        column: x => x.SupplierOrderId,
                        principalTable: "SupplierOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierOrders_OrderType",
                table: "SupplierOrders",
                column: "OrderType");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierOrders_PoNumber",
                table: "SupplierOrders",
                column: "PoNumber",
                unique: true,
                filter: "\"PoNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDocuments_SupplierOrderId_Kind",
                table: "OrderDocuments",
                columns: new[] { "SupplierOrderId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentContacts_Email",
                table: "PaymentContacts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentContacts_IsActive",
                table: "PaymentContacts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_PaymentContactId",
                table: "PaymentRequests",
                column: "PaymentContactId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_SentAt",
                table: "PaymentRequests",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_SupplierOrderId",
                table: "PaymentRequests",
                column: "SupplierOrderId");

            // Purchase-order numbers come from a sequence, not MAX(PoNumber) + 1: nextval is
            // atomic, so two users saving at the same instant cannot be handed the same number,
            // and it never rolls back, so a number is never reused.
            migrationBuilder.Sql("CREATE SEQUENCE IF NOT EXISTS po_number_seq START 1;");

            // Backfill every order that already exists, oldest first, so the numbers follow the
            // order the work actually happened in.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "Id") AS seq
                    FROM "SupplierOrders"
                    WHERE "PoNumber" IS NULL
                )
                UPDATE "SupplierOrders" o
                SET "PoNumber" = 'PO-' || EXTRACT(YEAR FROM CURRENT_DATE)::int || '-' ||
                                 LPAD(numbered.seq::text, 4, '0')
                FROM numbered
                WHERE o."Id" = numbered."Id";
                """);

            // Move the sequence past everything just handed out, so the next new order cannot
            // collide with a backfilled number.
            migrationBuilder.Sql("""
                SELECT setval('po_number_seq',
                              GREATEST((SELECT COUNT(*) FROM "SupplierOrders"), 1));
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderDocuments");

            migrationBuilder.DropTable(
                name: "PaymentRequests");

            migrationBuilder.DropTable(
                name: "PaymentContacts");

            migrationBuilder.DropIndex(
                name: "IX_SupplierOrders_OrderType",
                table: "SupplierOrders");

            migrationBuilder.DropIndex(
                name: "IX_SupplierOrders_PoNumber",
                table: "SupplierOrders");

            migrationBuilder.DropColumn(
                name: "OrderType",
                table: "SupplierOrders");

            migrationBuilder.DropColumn(
                name: "PoNumber",
                table: "SupplierOrders");

            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS po_number_seq;");

        }
    }
}
