using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoidsAndComps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "comp_reason",
                schema: "res",
                table: "order_line",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_comped",
                schema: "res",
                table: "order_line",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_voided",
                schema: "res",
                table: "order_line",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                schema: "res",
                table: "order_line",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "comp_reason",
                schema: "res",
                table: "order_line");

            migrationBuilder.DropColumn(
                name: "is_comped",
                schema: "res",
                table: "order_line");

            migrationBuilder.DropColumn(
                name: "is_voided",
                schema: "res",
                table: "order_line");

            migrationBuilder.DropColumn(
                name: "void_reason",
                schema: "res",
                table: "order_line");
        }
    }
}
