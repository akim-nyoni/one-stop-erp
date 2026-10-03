using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemWeightAndVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unit_volume_m3",
                schema: "inv",
                table: "item",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_weight_kg",
                schema: "inv",
                table: "item",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "unit_volume_m3",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "unit_weight_kg",
                schema: "inv",
                table: "item");
        }
    }
}
