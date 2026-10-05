using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payslip_company_id_employee_id_period_label",
                schema: "pay",
                table: "payslip");

            migrationBuilder.DropIndex(
                name: "ix_payroll_run_company_id_period_label",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.AddColumn<int>(
                name: "run_kind",
                schema: "pay",
                table: "payslip",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "run_reference",
                schema: "pay",
                table: "payslip",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "document_number",
                schema: "hr",
                table: "payroll_run",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "kind",
                schema: "hr",
                table: "payroll_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                schema: "hr",
                table: "payroll_run",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reversal_approved_by_user_id",
                schema: "hr",
                table: "payroll_run",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reversed_by",
                schema: "hr",
                table: "payroll_run",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "reversed_on",
                schema: "hr",
                table: "payroll_run",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "backpay_line",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    for_period = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    element_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    run_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backpay_line", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "legacy_payslip_line",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    period_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    employee_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    element_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legacy_payslip_line", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payslip_company_id_employee_id_period_label_run_reference",
                schema: "pay",
                table: "payslip",
                columns: new[] { "company_id", "employee_id", "period_label", "run_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_company_id_period_label",
                schema: "hr",
                table: "payroll_run",
                columns: new[] { "company_id", "period_label" },
                unique: true,
                filter: "kind = 0 AND reversed_on IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_backpay_line_company_id_paid_on",
                schema: "pay",
                table: "backpay_line",
                columns: new[] { "company_id", "paid_on" });

            migrationBuilder.CreateIndex(
                name: "ix_legacy_payslip_line_company_id_period_label",
                schema: "pay",
                table: "legacy_payslip_line",
                columns: new[] { "company_id", "period_label" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backpay_line",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "legacy_payslip_line",
                schema: "pay");

            migrationBuilder.DropIndex(
                name: "ix_payslip_company_id_employee_id_period_label_run_reference",
                schema: "pay",
                table: "payslip");

            migrationBuilder.DropIndex(
                name: "ix_payroll_run_company_id_period_label",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "run_kind",
                schema: "pay",
                table: "payslip");

            migrationBuilder.DropColumn(
                name: "run_reference",
                schema: "pay",
                table: "payslip");

            migrationBuilder.DropColumn(
                name: "document_number",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "reason",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "reversal_approved_by_user_id",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "reversed_by",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "reversed_on",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_company_id_employee_id_period_label",
                schema: "pay",
                table: "payslip",
                columns: new[] { "company_id", "employee_id", "period_label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_company_id_period_label",
                schema: "hr",
                table: "payroll_run",
                columns: new[] { "company_id", "period_label" },
                unique: true);
        }
    }
}
