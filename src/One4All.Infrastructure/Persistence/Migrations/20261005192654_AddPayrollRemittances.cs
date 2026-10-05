using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRemittances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "supplier_id",
                schema: "hr",
                table: "statutory_scheme",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "supplier_id",
                schema: "hr",
                table: "garnishee_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "raise_payroll_remittances",
                schema: "sys",
                table: "company",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "remittance_invoice",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    run_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    period_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payee = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    raised_on = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remittance_invoice", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_remittance_invoice_company_id_run_reference",
                schema: "pay",
                table: "remittance_invoice",
                columns: new[] { "company_id", "run_reference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "remittance_invoice",
                schema: "pay");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                schema: "hr",
                table: "statutory_scheme");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                schema: "hr",
                table: "garnishee_order");

            migrationBuilder.DropColumn(
                name: "raise_payroll_remittances",
                schema: "sys",
                table: "company");
        }
    }
}
