using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingRunChunkedPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_posted",
                schema: "mun",
                table: "billing_run_line",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "posted_open_item_id",
                schema: "mun",
                table: "billing_run_line",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_posted",
                schema: "mun",
                table: "billing_run_line");

            migrationBuilder.DropColumn(
                name: "posted_open_item_id",
                schema: "mun",
                table: "billing_run_line");
        }
    }
}
