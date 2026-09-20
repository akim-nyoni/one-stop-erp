using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptCancellationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                schema: "rcp",
                table: "receipt",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "cancelled_by_user_id",
                schema: "rcp",
                table: "receipt",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                schema: "rcp",
                table: "receipt");

            migrationBuilder.DropColumn(
                name: "cancelled_by_user_id",
                schema: "rcp",
                table: "receipt");
        }
    }
}
