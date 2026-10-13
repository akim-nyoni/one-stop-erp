using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesDiscountControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "list_unit_price",
                schema: "sop",
                table: "sales_order_line",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "awaiting_discount_approval",
                schema: "sop",
                table: "sales_order",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "discount_authorised_by_user_id",
                schema: "sop",
                table: "sales_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_sales_discount_percent",
                schema: "sys",
                table: "company",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "list_unit_price",
                schema: "sop",
                table: "sales_order_line");

            migrationBuilder.DropColumn(
                name: "awaiting_discount_approval",
                schema: "sop",
                table: "sales_order");

            migrationBuilder.DropColumn(
                name: "discount_authorised_by_user_id",
                schema: "sop",
                table: "sales_order");

            migrationBuilder.DropColumn(
                name: "max_sales_discount_percent",
                schema: "sys",
                table: "company");
        }
    }
}
