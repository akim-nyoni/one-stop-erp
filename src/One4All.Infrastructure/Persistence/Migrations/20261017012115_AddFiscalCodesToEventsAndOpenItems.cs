using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalCodesToEventsAndOpenItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fiscal_code",
                schema: "trd",
                table: "open_item",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "fiscal_queued",
                schema: "trd",
                table: "open_item",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_verification",
                schema: "trd",
                table: "open_item",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_code",
                schema: "pos",
                table: "event",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "fiscal_queued",
                schema: "pos",
                table: "event",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_verification",
                schema: "pos",
                table: "event",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fiscal_code",
                schema: "trd",
                table: "open_item");

            migrationBuilder.DropColumn(
                name: "fiscal_queued",
                schema: "trd",
                table: "open_item");

            migrationBuilder.DropColumn(
                name: "fiscal_verification",
                schema: "trd",
                table: "open_item");

            migrationBuilder.DropColumn(
                name: "fiscal_code",
                schema: "pos",
                table: "event");

            migrationBuilder.DropColumn(
                name: "fiscal_queued",
                schema: "pos",
                table: "event");

            migrationBuilder.DropColumn(
                name: "fiscal_verification",
                schema: "pos",
                table: "event");
        }
    }
}
