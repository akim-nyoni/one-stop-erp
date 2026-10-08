using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetConventionsAndInvoiceSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "convention",
                schema: "fa",
                table: "asset_class",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "convention",
                schema: "fa",
                table: "asset",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "source_account_code",
                schema: "fa",
                table: "asset",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "source_open_item_id",
                schema: "fa",
                table: "asset",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_source_open_item_id",
                schema: "fa",
                table: "asset",
                column: "source_open_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asset_source_open_item_id",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "convention",
                schema: "fa",
                table: "asset_class");

            migrationBuilder.DropColumn(
                name: "convention",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "source_account_code",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "source_open_item_id",
                schema: "fa",
                table: "asset");
        }
    }
}
