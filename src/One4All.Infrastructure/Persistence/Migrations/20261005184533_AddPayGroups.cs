using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payroll_run_company_id_period_label",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.AddColumn<string>(
                name: "pay_group",
                schema: "pay",
                table: "payslip",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "pay_group",
                schema: "hr",
                table: "payroll_run",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "pay_group",
                schema: "hr",
                table: "employee",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_company_id_pay_group_period_label",
                schema: "hr",
                table: "payroll_run",
                columns: new[] { "company_id", "pay_group", "period_label" },
                unique: true,
                filter: "kind = 0 AND reversed_on IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payroll_run_company_id_pay_group_period_label",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "pay_group",
                schema: "pay",
                table: "payslip");

            migrationBuilder.DropColumn(
                name: "pay_group",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "pay_group",
                schema: "hr",
                table: "employee");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_company_id_period_label",
                schema: "hr",
                table: "payroll_run",
                columns: new[] { "company_id", "period_label" },
                unique: true,
                filter: "kind = 0 AND reversed_on IS NULL");
        }
    }
}
