using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLaybyTaxAndLicenceDeferral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                schema: "ar",
                table: "layby_plan",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deferred_income_account_code",
                schema: "mun",
                table: "council_licence",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tax_code",
                schema: "ar",
                table: "layby_plan");

            migrationBuilder.DropColumn(
                name: "deferred_income_account_code",
                schema: "mun",
                table: "council_licence");
        }
    }
}
