using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixedAssetTagNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tag_number",
                schema: "fa",
                table: "asset",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_company_id_tag_number",
                schema: "fa",
                table: "asset",
                columns: new[] { "company_id", "tag_number" },
                unique: true,
                filter: "tag_number IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asset_company_id_tag_number",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "tag_number",
                schema: "fa",
                table: "asset");
        }
    }
}
