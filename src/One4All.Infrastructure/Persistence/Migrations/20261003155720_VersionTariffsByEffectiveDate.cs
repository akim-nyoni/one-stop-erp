using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionTariffsByEffectiveDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tariff_company_id_code",
                schema: "mun",
                table: "tariff");

            migrationBuilder.CreateIndex(
                name: "ix_tariff_company_id_code_effective_from",
                schema: "mun",
                table: "tariff",
                columns: new[] { "company_id", "code", "effective_from" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tariff_company_id_code_effective_from",
                schema: "mun",
                table: "tariff");

            migrationBuilder.CreateIndex(
                name: "ix_tariff_company_id_code",
                schema: "mun",
                table: "tariff",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
