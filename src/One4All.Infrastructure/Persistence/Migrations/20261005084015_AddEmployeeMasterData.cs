using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_account_number",
                schema: "hr",
                table: "employee",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_branch",
                schema: "hr",
                table: "employee",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                schema: "hr",
                table: "employee",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "contract_end_date",
                schema: "hr",
                table: "employee",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                schema: "hr",
                table: "employee",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gender",
                schema: "hr",
                table: "employee",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "grade",
                schema: "hr",
                table: "employee",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                schema: "hr",
                table: "employee",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_salary_held",
                schema: "hr",
                table: "employee",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "mobile_money_number",
                schema: "hr",
                table: "employee",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "national_id_number",
                schema: "hr",
                table: "employee",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "notch",
                schema: "hr",
                table: "employee",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_method",
                schema: "hr",
                table: "employee",
                type: "integer",
                nullable: false,
                defaultValue: 1); // existing employees are paid by bank transfer

            migrationBuilder.AddColumn<DateOnly>(
                name: "probation_end_date",
                schema: "hr",
                table: "employee",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_number",
                schema: "hr",
                table: "employee",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "termination_date",
                schema: "hr",
                table: "employee",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "termination_reason",
                schema: "hr",
                table: "employee",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "work_permit_expiry",
                schema: "hr",
                table: "employee",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_account_number",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "bank_branch",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "bank_name",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "contract_end_date",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "gender",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "grade",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "is_salary_held",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "mobile_money_number",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "national_id_number",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "notch",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "probation_end_date",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "tax_number",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "termination_date",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "termination_reason",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "work_permit_expiry",
                schema: "hr",
                table: "employee");
        }
    }
}
