using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldCollectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_field_collector",
                schema: "rcp",
                table: "station",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "receipt_range_from",
                schema: "rcp",
                table: "station",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "receipt_range_to",
                schema: "rcp",
                table: "station",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "route_code",
                schema: "rcp",
                table: "station",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_field_collector",
                schema: "rcp",
                table: "station");

            migrationBuilder.DropColumn(
                name: "receipt_range_from",
                schema: "rcp",
                table: "station");

            migrationBuilder.DropColumn(
                name: "receipt_range_to",
                schema: "rcp",
                table: "station");

            migrationBuilder.DropColumn(
                name: "route_code",
                schema: "rcp",
                table: "station");
        }
    }
}
