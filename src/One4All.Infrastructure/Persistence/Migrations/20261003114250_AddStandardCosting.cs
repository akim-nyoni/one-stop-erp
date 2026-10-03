using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStandardCosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "price_variance_account_code",
                schema: "inv",
                table: "item",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "standard_cost",
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
                name: "price_variance_account_code",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "standard_cost",
                schema: "inv",
                table: "item");
        }
    }
}
