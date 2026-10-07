using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOverReceiptTolerance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "over_receipt_tolerance_percent",
                schema: "ap",
                table: "supplier",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "over_receipt_authorised_by_user_id",
                schema: "pop",
                table: "grn",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "over_receipt_tolerance_percent",
                schema: "sys",
                table: "company",
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
                name: "over_receipt_tolerance_percent",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "over_receipt_authorised_by_user_id",
                schema: "pop",
                table: "grn");

            migrationBuilder.DropColumn(
                name: "over_receipt_tolerance_percent",
                schema: "sys",
                table: "company");
        }
    }
}
