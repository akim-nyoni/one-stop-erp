using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatchWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "weight",
                schema: "inv",
                table: "stock_movement",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "weight_on_hand",
                schema: "inv",
                table: "stock_balance",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "catch_weight_unit",
                schema: "inv",
                table: "item",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "nominal_weight",
                schema: "inv",
                table: "item",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "weight_tolerance_percent",
                schema: "inv",
                table: "item",
                type: "numeric(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "weight",
                schema: "inv",
                table: "stock_movement");

            migrationBuilder.DropColumn(
                name: "weight_on_hand",
                schema: "inv",
                table: "stock_balance");

            migrationBuilder.DropColumn(
                name: "catch_weight_unit",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "nominal_weight",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "weight_tolerance_percent",
                schema: "inv",
                table: "item");
        }
    }
}
