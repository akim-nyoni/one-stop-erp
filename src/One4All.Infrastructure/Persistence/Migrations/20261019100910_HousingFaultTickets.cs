using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HousingFaultTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "rental_unit_id",
                schema: "mun",
                table: "service_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "repair_number",
                schema: "mun",
                table: "service_ticket",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_ticket_rental_unit_id",
                schema: "mun",
                table: "service_ticket",
                column: "rental_unit_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_service_ticket_rental_unit_id",
                schema: "mun",
                table: "service_ticket");

            migrationBuilder.DropColumn(
                name: "rental_unit_id",
                schema: "mun",
                table: "service_ticket");

            migrationBuilder.DropColumn(
                name: "repair_number",
                schema: "mun",
                table: "service_ticket");
        }
    }
}
