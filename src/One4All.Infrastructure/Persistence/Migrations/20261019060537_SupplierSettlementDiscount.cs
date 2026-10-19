using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierSettlementDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "settlement_discount_days",
                schema: "ap",
                table: "supplier",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "settlement_discount_percent",
                schema: "ap",
                table: "supplier",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "settlement_discount_received_account_code",
                schema: "sys",
                table: "company",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "settlement_discount_days",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "settlement_discount_percent",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "settlement_discount_received_account_code",
                schema: "sys",
                table: "company");
        }
    }
}
