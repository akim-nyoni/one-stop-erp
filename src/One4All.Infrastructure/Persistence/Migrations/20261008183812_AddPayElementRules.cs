using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayElementRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "basis_group",
                schema: "pay",
                table: "element",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ceiling",
                schema: "pay",
                table: "element",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department_accounts",
                schema: "pay",
                table: "element",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "floor",
                schema: "pay",
                table: "element",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "group",
                schema: "pay",
                table: "element",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "leave_pay_base",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "overtime_base",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "pensionable",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "round_to",
                schema: "pay",
                table: "element",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rounding",
                schema: "pay",
                table: "element",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "subject_to_social_security",
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
                name: "basis_group",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "ceiling",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "department_accounts",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "floor",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "group",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "leave_pay_base",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "overtime_base",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "pensionable",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "round_to",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "rounding",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "subject_to_social_security",
                schema: "pay",
                table: "element");
        }
    }
}
