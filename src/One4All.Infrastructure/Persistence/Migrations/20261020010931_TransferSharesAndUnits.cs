using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransferSharesAndUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sectional_scheme_code",
                schema: "hse",
                table: "transfer_application",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "share_percent",
                schema: "hse",
                table: "transfer_application",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit_number",
                schema: "hse",
                table: "transfer_application",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sectional_scheme_code",
                schema: "hse",
                table: "transfer_application");

            migrationBuilder.DropColumn(
                name: "share_percent",
                schema: "hse",
                table: "transfer_application");

            migrationBuilder.DropColumn(
                name: "unit_number",
                schema: "hse",
                table: "transfer_application");
        }
    }
}
