using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFloorPlanAndTabWarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "plan_x",
                schema: "res",
                table: "table",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "plan_y",
                schema: "res",
                table: "table",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "shape",
                schema: "res",
                table: "table",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "tab_warning_amount",
                schema: "res",
                table: "policy",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "tab_warning_minutes",
                schema: "res",
                table: "policy",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "plan_x",
                schema: "res",
                table: "table");

            migrationBuilder.DropColumn(
                name: "plan_y",
                schema: "res",
                table: "table");

            migrationBuilder.DropColumn(
                name: "shape",
                schema: "res",
                table: "table");

            migrationBuilder.DropColumn(
                name: "tab_warning_amount",
                schema: "res",
                table: "policy");

            migrationBuilder.DropColumn(
                name: "tab_warning_minutes",
                schema: "res",
                table: "policy");
        }
    }
}
