using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEffectiveDatedPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_price_list_line_price_list_id_item_id_minimum_quantity",
                schema: "inv",
                table: "price_list_line");

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_from",
                schema: "inv",
                table: "price_list_line",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_price_list_line_price_list_id_item_id_minimum_quantity_effe",
                schema: "inv",
                table: "price_list_line",
                columns: new[] { "price_list_id", "item_id", "minimum_quantity", "effective_from" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_price_list_line_price_list_id_item_id_minimum_quantity_effe",
                schema: "inv",
                table: "price_list_line");

            migrationBuilder.DropColumn(
                name: "effective_from",
                schema: "inv",
                table: "price_list_line");

            migrationBuilder.CreateIndex(
                name: "ix_price_list_line_price_list_id_item_id_minimum_quantity",
                schema: "inv",
                table: "price_list_line",
                columns: new[] { "price_list_id", "item_id", "minimum_quantity" },
                unique: true);
        }
    }
}
