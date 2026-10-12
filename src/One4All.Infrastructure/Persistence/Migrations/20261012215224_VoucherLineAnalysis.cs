using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VoucherLineAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cost_centre",
                schema: "vou",
                table: "accounting_voucher_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department",
                schema: "vou",
                table: "accounting_voucher_line",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project",
                schema: "vou",
                table: "accounting_voucher_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cost_centre",
                schema: "vou",
                table: "accounting_voucher_line");

            migrationBuilder.DropColumn(
                name: "department",
                schema: "vou",
                table: "accounting_voucher_line");

            migrationBuilder.DropColumn(
                name: "project",
                schema: "vou",
                table: "accounting_voucher_line");
        }
    }
}
