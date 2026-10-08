using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecificCostAndUsageVariance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "inv",
                table: "lot_movement",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "usage_variance_account_code",
                schema: "inv",
                table: "item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "usage_variance",
                schema: "inv",
                table: "assembly_run",
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
                name: "unit_cost",
                schema: "inv",
                table: "lot_movement");

            migrationBuilder.DropColumn(
                name: "usage_variance_account_code",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "usage_variance",
                schema: "inv",
                table: "assembly_run");
        }
    }
}
