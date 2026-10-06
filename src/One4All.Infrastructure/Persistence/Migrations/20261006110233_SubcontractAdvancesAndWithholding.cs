using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubcontractAdvancesAndWithholding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "advance_recovered",
                schema: "job",
                table: "subcontract_certificate",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "withheld",
                schema: "job",
                table: "subcontract_certificate",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "advance_account_code",
                schema: "job",
                table: "subcontract",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "advance_paid",
                schema: "job",
                table: "subcontract",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "advance_recovered",
                schema: "job",
                table: "subcontract",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "advance_recovery_percent",
                schema: "job",
                table: "subcontract",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "withholding_tax_code",
                schema: "job",
                table: "subcontract",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "advance_recovered",
                schema: "job",
                table: "subcontract_certificate");

            migrationBuilder.DropColumn(
                name: "withheld",
                schema: "job",
                table: "subcontract_certificate");

            migrationBuilder.DropColumn(
                name: "advance_account_code",
                schema: "job",
                table: "subcontract");

            migrationBuilder.DropColumn(
                name: "advance_paid",
                schema: "job",
                table: "subcontract");

            migrationBuilder.DropColumn(
                name: "advance_recovered",
                schema: "job",
                table: "subcontract");

            migrationBuilder.DropColumn(
                name: "advance_recovery_percent",
                schema: "job",
                table: "subcontract");

            migrationBuilder.DropColumn(
                name: "withholding_tax_code",
                schema: "job",
                table: "subcontract");
        }
    }
}
