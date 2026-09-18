using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptReversalAnchors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cash_account_code",
                schema: "rcp",
                table: "receipt",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "debtor_open_item_id",
                schema: "rcp",
                table: "receipt",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cash_account_code",
                schema: "rcp",
                table: "receipt");

            migrationBuilder.DropColumn(
                name: "debtor_open_item_id",
                schema: "rcp",
                table: "receipt");
        }
    }
}
