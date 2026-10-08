using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeePlacementAndPersonalDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "branch_id",
                schema: "hr",
                table: "employee",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fund",
                schema: "hr",
                table: "employee",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "marital_status",
                schema: "hr",
                table: "employee",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "nationality",
                schema: "hr",
                table: "employee",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "next_of_kin_name",
                schema: "hr",
                table: "employee",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "next_of_kin_phone",
                schema: "hr",
                table: "employee",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "next_of_kin_relationship",
                schema: "hr",
                table: "employee",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project",
                schema: "hr",
                table: "employee",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reports_to_employee_id",
                schema: "hr",
                table: "employee",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "social_security_number",
                schema: "hr",
                table: "employee",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "union_membership",
                schema: "hr",
                table: "employee",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "fund",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "marital_status",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "nationality",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "next_of_kin_name",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "next_of_kin_phone",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "next_of_kin_relationship",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "project",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "reports_to_employee_id",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "social_security_number",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "union_membership",
                schema: "hr",
                table: "employee");
        }
    }
}
