using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanRepaymentFromPay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "deduct_from_pay",
                schema: "pay",
                table: "employee_loan",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "suspended",
                schema: "pay",
                table: "employee_loan",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "staff_loan_interest_account_code",
                schema: "sys",
                table: "company",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deduct_from_pay",
                schema: "pay",
                table: "employee_loan");

            migrationBuilder.DropColumn(
                name: "suspended",
                schema: "pay",
                table: "employee_loan");

            migrationBuilder.DropColumn(
                name: "staff_loan_interest_account_code",
                schema: "sys",
                table: "company");
        }
    }
}
