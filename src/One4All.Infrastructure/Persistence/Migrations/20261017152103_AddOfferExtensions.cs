using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "extension_reason",
                schema: "hse",
                table: "stand_allocation",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "offer_extensions",
                schema: "hse",
                table: "stand_allocation",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extension_reason",
                schema: "hse",
                table: "stand_allocation");

            migrationBuilder.DropColumn(
                name: "offer_extensions",
                schema: "hse",
                table: "stand_allocation");
        }
    }
}
