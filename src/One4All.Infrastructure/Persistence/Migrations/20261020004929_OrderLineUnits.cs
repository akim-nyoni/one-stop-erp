using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderLineUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "unit_code",
                schema: "sop",
                table: "sales_order_line",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_factor",
                schema: "sop",
                table: "sales_order_line",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "unit_code",
                schema: "pop",
                table: "purchase_order_line",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_factor",
                schema: "pop",
                table: "purchase_order_line",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: false,
                defaultValue: 1m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "unit_code",
                schema: "sop",
                table: "sales_order_line");

            migrationBuilder.DropColumn(
                name: "unit_factor",
                schema: "sop",
                table: "sales_order_line");

            migrationBuilder.DropColumn(
                name: "unit_code",
                schema: "pop",
                table: "purchase_order_line");

            migrationBuilder.DropColumn(
                name: "unit_factor",
                schema: "pop",
                table: "purchase_order_line");
        }
    }
}
