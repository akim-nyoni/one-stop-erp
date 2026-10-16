using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayElementFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "balance",
                schema: "hr",
                table: "employee_pay_element",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "arrears_recoverable",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "pro_rated",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "terminal_benefit",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "union_dues",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "balance",
                schema: "hr",
                table: "employee_pay_element");

            migrationBuilder.DropColumn(
                name: "arrears_recoverable",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "pro_rated",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "terminal_benefit",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "union_dues",
                schema: "pay",
                table: "element");
        }
    }
}
