using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxTableBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "cumulative_tax",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_annual_payment",
                schema: "pay",
                table: "element",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tax_table_code",
                schema: "pay",
                table: "element",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cumulative_tax",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "is_annual_payment",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "tax_table_code",
                schema: "pay",
                table: "element");
        }
    }
}
