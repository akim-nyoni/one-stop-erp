using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "reserved_quantity",
                schema: "sop",
                table: "sales_order_line",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "reserved_warehouse_id",
                schema: "sop",
                table: "sales_order_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_line_reserved",
                schema: "sop",
                table: "sales_order_line",
                columns: new[] { "item_id", "reserved_warehouse_id" },
                filter: "reserved_quantity > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sales_order_line_reserved",
                schema: "sop",
                table: "sales_order_line");

            migrationBuilder.DropColumn(
                name: "reserved_quantity",
                schema: "sop",
                table: "sales_order_line");

            migrationBuilder.DropColumn(
                name: "reserved_warehouse_id",
                schema: "sop",
                table: "sales_order_line");
        }
    }
}
