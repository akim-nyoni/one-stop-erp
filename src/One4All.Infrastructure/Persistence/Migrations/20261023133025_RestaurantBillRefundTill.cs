using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestaurantBillRefundTill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cash_account_code",
                schema: "res",
                table: "bill",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "refund_till_session_id",
                schema: "res",
                table: "bill",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cash_account_code",
                schema: "res",
                table: "bill");

            migrationBuilder.DropColumn(
                name: "refund_till_session_id",
                schema: "res",
                table: "bill");
        }
    }
}
