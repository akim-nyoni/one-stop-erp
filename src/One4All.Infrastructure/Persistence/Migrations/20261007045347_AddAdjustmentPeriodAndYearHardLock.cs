using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdjustmentPeriodAndYearHardLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_hard_locked",
                schema: "sys",
                table: "fiscal_year",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_adjustment",
                schema: "sys",
                table: "fiscal_period",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_hard_locked",
                schema: "sys",
                table: "fiscal_year");

            migrationBuilder.DropColumn(
                name: "is_adjustment",
                schema: "sys",
                table: "fiscal_period");
        }
    }
}
