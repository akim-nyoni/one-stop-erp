using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "logo_height",
                schema: "pos",
                table: "receipt_layout",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "logo_raster",
                schema: "pos",
                table: "receipt_layout",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "logo_width_bytes",
                schema: "pos",
                table: "receipt_layout",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "logo_height",
                schema: "pos",
                table: "receipt_layout");

            migrationBuilder.DropColumn(
                name: "logo_raster",
                schema: "pos",
                table: "receipt_layout");

            migrationBuilder.DropColumn(
                name: "logo_width_bytes",
                schema: "pos",
                table: "receipt_layout");
        }
    }
}
