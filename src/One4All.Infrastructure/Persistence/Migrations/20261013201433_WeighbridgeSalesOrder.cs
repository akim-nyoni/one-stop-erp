using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WeighbridgeSalesOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "sales_order_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_weighbridge_ticket_sales_order_id",
                schema: "job",
                table: "weighbridge_ticket",
                column: "sales_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_weighbridge_ticket_sales_order_id",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "sales_order_id",
                schema: "job",
                table: "weighbridge_ticket");
        }
    }
}
