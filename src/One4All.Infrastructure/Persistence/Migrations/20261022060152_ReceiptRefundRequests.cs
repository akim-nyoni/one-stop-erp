using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptRefundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "allocated_by_user_id",
                schema: "rcp",
                table: "suspense_allocation",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_number",
                schema: "rcp",
                table: "suspense_allocation",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "receipt_refund_request",
                schema: "rcp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    bank_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    journal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_refund_request", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_suspense_allocation_company_id_receipt_number",
                schema: "rcp",
                table: "suspense_allocation",
                columns: new[] { "company_id", "receipt_number" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_refund_request_company_id_customer_id_status",
                schema: "rcp",
                table: "receipt_refund_request",
                columns: new[] { "company_id", "customer_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_refund_request",
                schema: "rcp");

            migrationBuilder.DropIndex(
                name: "ix_suspense_allocation_company_id_receipt_number",
                schema: "rcp",
                table: "suspense_allocation");

            migrationBuilder.DropColumn(
                name: "allocated_by_user_id",
                schema: "rcp",
                table: "suspense_allocation");

            migrationBuilder.DropColumn(
                name: "receipt_number",
                schema: "rcp",
                table: "suspense_allocation");
        }
    }
}
