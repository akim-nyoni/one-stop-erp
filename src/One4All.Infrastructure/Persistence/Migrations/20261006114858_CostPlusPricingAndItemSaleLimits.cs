using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CostPlusPricingAndItemSaleLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "age_verified",
                schema: "pos",
                table: "sale",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "prescription_reference",
                schema: "pos",
                table: "sale",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cost_plus_basis",
                schema: "inv",
                table: "price_list",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_plus_percent",
                schema: "inv",
                table: "price_list",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "cost_plus_round_to",
                schema: "inv",
                table: "price_list",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "cost_plus_rounding",
                schema: "inv",
                table: "price_list",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "max_quantity_per_sale",
                schema: "inv",
                table: "item",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "minimum_age",
                schema: "inv",
                table: "item",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_prescription",
                schema: "inv",
                table: "item",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "age_verified",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "prescription_reference",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "cost_plus_basis",
                schema: "inv",
                table: "price_list");

            migrationBuilder.DropColumn(
                name: "cost_plus_percent",
                schema: "inv",
                table: "price_list");

            migrationBuilder.DropColumn(
                name: "cost_plus_round_to",
                schema: "inv",
                table: "price_list");

            migrationBuilder.DropColumn(
                name: "cost_plus_rounding",
                schema: "inv",
                table: "price_list");

            migrationBuilder.DropColumn(
                name: "max_quantity_per_sale",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "minimum_age",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "requires_prescription",
                schema: "inv",
                table: "item");
        }
    }
}
