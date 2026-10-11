using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetHeaderCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "budget_rate",
                schema: "bud",
                table: "budget",
                type: "numeric(19,8)",
                precision: 19,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "bud",
                table: "budget",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "budget_rate",
                schema: "bud",
                table: "budget");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "bud",
                table: "budget");
        }
    }
}
