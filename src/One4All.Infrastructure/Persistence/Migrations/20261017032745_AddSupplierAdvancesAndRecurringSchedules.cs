using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierAdvancesAndRecurringSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_reverse",
                schema: "gl",
                table: "recurring_journal",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "gl",
                table: "recurring_journal",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                schema: "gl",
                table: "recurring_journal",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "interval_months",
                schema: "gl",
                table: "recurring_journal",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "max_occurrences",
                schema: "gl",
                table: "recurring_journal",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "occurrences",
                schema: "gl",
                table: "recurring_journal",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                schema: "gl",
                table: "recurring_journal",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supplier_advance",
                schema: "trd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    advance_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    applied = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    refunded = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    advance_account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bank_account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    purchase_order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    narrative = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    applications = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_advance", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_advance_company_id_document_number",
                schema: "trd",
                table: "supplier_advance",
                columns: new[] { "company_id", "document_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_advance_company_id_supplier_id_status",
                schema: "trd",
                table: "supplier_advance",
                columns: new[] { "company_id", "supplier_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_advance",
                schema: "trd");

            migrationBuilder.DropColumn(
                name: "auto_reverse",
                schema: "gl",
                table: "recurring_journal");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "gl",
                table: "recurring_journal");

            migrationBuilder.DropColumn(
                name: "end_date",
                schema: "gl",
                table: "recurring_journal");

            migrationBuilder.DropColumn(
                name: "interval_months",
                schema: "gl",
                table: "recurring_journal");

            migrationBuilder.DropColumn(
                name: "max_occurrences",
                schema: "gl",
                table: "recurring_journal");

            migrationBuilder.DropColumn(
                name: "occurrences",
                schema: "gl",
                table: "recurring_journal");

            migrationBuilder.DropColumn(
                name: "start_date",
                schema: "gl",
                table: "recurring_journal");
        }
    }
}
