using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReplenishmentPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "forecast_window_days",
                schema: "inv",
                table: "item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "lead_time_days",
                schema: "inv",
                table: "item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "maximum_stock",
                schema: "inv",
                table: "item",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "forecast_window_days",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "lead_time_days",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "maximum_stock",
                schema: "inv",
                table: "item");
        }
    }
}
