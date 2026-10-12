using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollRunSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "closed_on",
                schema: "hr",
                table: "payroll_run",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "net_pay_account_code",
                schema: "hr",
                table: "payroll_run",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "paid_on",
                schema: "hr",
                table: "payroll_run",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_journal_number",
                schema: "hr",
                table: "payroll_run",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closed_on",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "net_pay_account_code",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "paid_on",
                schema: "hr",
                table: "payroll_run");

            migrationBuilder.DropColumn(
                name: "payment_journal_number",
                schema: "hr",
                table: "payroll_run");
        }
    }
}
