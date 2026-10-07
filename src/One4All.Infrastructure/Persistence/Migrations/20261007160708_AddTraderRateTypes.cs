using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTraderRateTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rate_type",
                schema: "ap",
                table: "supplier",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rate_type",
                schema: "ar",
                table: "customer",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rate_type",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "rate_type",
                schema: "ar",
                table: "customer");
        }
    }
}
