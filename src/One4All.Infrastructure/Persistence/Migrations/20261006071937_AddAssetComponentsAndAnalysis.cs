using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetComponentsAndAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cost_centre",
                schema: "fa",
                table: "asset",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department",
                schema: "fa",
                table: "asset",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "parent_asset_id",
                schema: "fa",
                table: "asset",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_parent_asset_id",
                schema: "fa",
                table: "asset",
                column: "parent_asset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asset_parent_asset_id",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "cost_centre",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "department",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "parent_asset_id",
                schema: "fa",
                table: "asset");
        }
    }
}
