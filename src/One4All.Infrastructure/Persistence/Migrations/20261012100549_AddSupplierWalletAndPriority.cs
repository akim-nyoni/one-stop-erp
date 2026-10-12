using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierWalletAndPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mobile_money_number",
                schema: "ap",
                table: "supplier_bank_change",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mobile_money_number",
                schema: "ap",
                table: "supplier",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_priority",
                schema: "ap",
                table: "supplier",
                type: "integer",
                nullable: false,
                defaultValue: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mobile_money_number",
                schema: "ap",
                table: "supplier_bank_change");

            migrationBuilder.DropColumn(
                name: "mobile_money_number",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "payment_priority",
                schema: "ap",
                table: "supplier");
        }
    }
}
