using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptAllocationOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "allocation_override",
                schema: "rcp",
                table: "receipt",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "allocation_override_reason",
                schema: "rcp",
                table: "receipt",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allocation_override",
                schema: "rcp",
                table: "receipt");

            migrationBuilder.DropColumn(
                name: "allocation_override_reason",
                schema: "rcp",
                table: "receipt");
        }
    }
}
