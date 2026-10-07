using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTariffBillingCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cycle",
                schema: "mun",
                table: "tariff",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "cycle_start_month",
                schema: "mun",
                table: "tariff",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "instalments",
                schema: "mun",
                table: "tariff",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cycle",
                schema: "mun",
                table: "tariff");

            migrationBuilder.DropColumn(
                name: "cycle_start_month",
                schema: "mun",
                table: "tariff");

            migrationBuilder.DropColumn(
                name: "instalments",
                schema: "mun",
                table: "tariff");
        }
    }
}
