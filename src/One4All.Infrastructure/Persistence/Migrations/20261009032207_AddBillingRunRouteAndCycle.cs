using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingRunRouteAndCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cycle",
                schema: "mun",
                table: "billing_run",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "route_code",
                schema: "mun",
                table: "billing_run",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cycle",
                schema: "mun",
                table: "billing_run");

            migrationBuilder.DropColumn(
                name: "route_code",
                schema: "mun",
                table: "billing_run");
        }
    }
}
