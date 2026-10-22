using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseOrderLineTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "invoiced_base_value",
                schema: "pop",
                table: "purchase_order_line",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "received_base_value",
                schema: "pop",
                table: "purchase_order_line",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                schema: "pop",
                table: "purchase_order_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Receipts and matches before this change accrued and cleared GRNI at the order price as it stood, so that is what each line holds.
            migrationBuilder.Sql(
                "UPDATE pop.purchase_order_line SET received_base_value = round(received_quantity * unit_price, 4), invoiced_base_value = round(invoiced_quantity * unit_price, 4);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invoiced_base_value",
                schema: "pop",
                table: "purchase_order_line");

            migrationBuilder.DropColumn(
                name: "received_base_value",
                schema: "pop",
                table: "purchase_order_line");

            migrationBuilder.DropColumn(
                name: "tax_code",
                schema: "pop",
                table: "purchase_order_line");
        }
    }
}
