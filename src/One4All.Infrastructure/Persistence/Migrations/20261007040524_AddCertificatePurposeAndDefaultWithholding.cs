using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificatePurposeAndDefaultWithholding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "purpose",
                schema: "gl",
                table: "tax_exemption_certificate",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "withholding_without_clearance_tax_code",
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
                name: "purpose",
                schema: "gl",
                table: "tax_exemption_certificate");

            migrationBuilder.DropColumn(
                name: "withholding_without_clearance_tax_code",
                schema: "sys",
                table: "company");
        }
    }
}
