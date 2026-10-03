using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakePayslipPeriodUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payslip_company_id_employee_id_period_label",
                schema: "pay",
                table: "payslip");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_company_id_employee_id_period_label",
                schema: "pay",
                table: "payslip",
                columns: new[] { "company_id", "employee_id", "period_label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payslip_company_id_employee_id_period_label",
                schema: "pay",
                table: "payslip");

            migrationBuilder.CreateIndex(
                name: "ix_payslip_company_id_employee_id_period_label",
                schema: "pay",
                table: "payslip",
                columns: new[] { "company_id", "employee_id", "period_label" });
        }
    }
}
