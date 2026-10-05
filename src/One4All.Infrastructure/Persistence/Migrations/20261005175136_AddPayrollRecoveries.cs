using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRecoveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "staff_employee_id",
                schema: "pos",
                table: "sale_tender",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "housing_tenancy_id",
                schema: "hr",
                table: "employee",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "recovers",
                schema: "pay",
                table: "element",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_sale_tender_staff_employee_id",
                schema: "pos",
                table: "sale_tender",
                column: "staff_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_housing_tenancy_id",
                schema: "hr",
                table: "employee",
                column: "housing_tenancy_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sale_tender_staff_employee_id",
                schema: "pos",
                table: "sale_tender");

            migrationBuilder.DropIndex(
                name: "ix_employee_housing_tenancy_id",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "staff_employee_id",
                schema: "pos",
                table: "sale_tender");

            migrationBuilder.DropColumn(
                name: "housing_tenancy_id",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "recovers",
                schema: "pay",
                table: "element");
        }
    }
}
