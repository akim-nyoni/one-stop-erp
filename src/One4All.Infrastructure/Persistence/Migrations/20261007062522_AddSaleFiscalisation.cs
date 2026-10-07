using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleFiscalisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "fiscal_attempts",
                schema: "pos",
                table: "sale",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_code",
                schema: "pos",
                table: "sale",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fiscal_day_number",
                schema: "pos",
                table: "sale",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_error",
                schema: "pos",
                table: "sale",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fiscal_status",
                schema: "pos",
                table: "sale",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "fiscal_verification",
                schema: "pos",
                table: "sale",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sale_company_id_fiscal_status",
                schema: "pos",
                table: "sale",
                columns: new[] { "company_id", "fiscal_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sale_company_id_fiscal_status",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "fiscal_attempts",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "fiscal_code",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "fiscal_day_number",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "fiscal_error",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "fiscal_status",
                schema: "pos",
                table: "sale");

            migrationBuilder.DropColumn(
                name: "fiscal_verification",
                schema: "pos",
                table: "sale");
        }
    }
}
