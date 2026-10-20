using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LevyDeferral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_deferred_income",
                schema: "gl",
                table: "prepayment_schedule",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "defer_months",
                schema: "hse",
                table: "development_levy",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deferred_income_account_code",
                schema: "hse",
                table: "development_levy",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_deferred_income",
                schema: "gl",
                table: "prepayment_schedule");

            migrationBuilder.DropColumn(
                name: "defer_months",
                schema: "hse",
                table: "development_levy");

            migrationBuilder.DropColumn(
                name: "deferred_income_account_code",
                schema: "hse",
                table: "development_levy");
        }
    }
}
