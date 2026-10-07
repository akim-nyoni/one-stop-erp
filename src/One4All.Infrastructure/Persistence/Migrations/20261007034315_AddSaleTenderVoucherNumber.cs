using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleTenderVoucherNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "voucher_number",
                schema: "pos",
                table: "sale_tender",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_tender_voucher_number",
                schema: "pos",
                table: "sale_tender",
                column: "voucher_number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sale_tender_voucher_number",
                schema: "pos",
                table: "sale_tender");

            migrationBuilder.DropColumn(
                name: "voucher_number",
                schema: "pos",
                table: "sale_tender");
        }
    }
}
