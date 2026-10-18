using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetRevaluationSurplus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "revaluation_loss_in_profit",
                schema: "fa",
                table: "asset",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "revaluation_reserve_account_code",
                schema: "fa",
                table: "asset",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "revaluation_surplus",
                schema: "fa",
                table: "asset",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revaluation_loss_in_profit",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "revaluation_reserve_account_code",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "revaluation_surplus",
                schema: "fa",
                table: "asset");
        }
    }
}
