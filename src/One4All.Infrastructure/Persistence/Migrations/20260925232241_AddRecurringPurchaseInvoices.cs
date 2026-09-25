using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringPurchaseInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_recurring_invoice",
                schema: "ar",
                table: "recurring_invoice");

            migrationBuilder.RenameIndex(
                name: "ix_recurring_invoice_company_id_is_active_next_run_date",
                schema: "ar",
                table: "recurring_invoice",
                newName: "ix_recurring_invoice_company_id_is_active_next_run_date1");

            migrationBuilder.AddPrimaryKey(
                name: "pk_recurring_invoice1",
                schema: "ar",
                table: "recurring_invoice",
                column: "id");

            migrationBuilder.CreateTable(
                name: "recurring_invoice",
                schema: "ap",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    expense_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    tax_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    interval_months = table.Column<int>(type: "integer", nullable: false),
                    next_run_date = table.Column<DateOnly>(type: "date", nullable: false),
                    narrative = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_invoice", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_invoice_company_id_is_active_next_run_date",
                schema: "ap",
                table: "recurring_invoice",
                columns: new[] { "company_id", "is_active", "next_run_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_invoice",
                schema: "ap");

            migrationBuilder.DropPrimaryKey(
                name: "pk_recurring_invoice1",
                schema: "ar",
                table: "recurring_invoice");

            migrationBuilder.RenameIndex(
                name: "ix_recurring_invoice_company_id_is_active_next_run_date1",
                schema: "ar",
                table: "recurring_invoice",
                newName: "ix_recurring_invoice_company_id_is_active_next_run_date");

            migrationBuilder.AddPrimaryKey(
                name: "pk_recurring_invoice",
                schema: "ar",
                table: "recurring_invoice",
                column: "id");
        }
    }
}
