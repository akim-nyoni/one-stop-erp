using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WeighbridgePriceOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "price_override_authorised_by_user_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_override_per_tonne",
                schema: "job",
                table: "weighbridge_ticket",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "price_override_authorised_by_user_id",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "price_override_per_tonne",
                schema: "job",
                table: "weighbridge_ticket");
        }
    }
}
