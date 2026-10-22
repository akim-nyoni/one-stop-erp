using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestaurantTillAndKitchenControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cancel_authorised_by_user_id",
                schema: "res",
                table: "order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_amount",
                schema: "res",
                table: "bill",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "reversal_authorised_by_user_id",
                schema: "res",
                table: "bill",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "till_session_id",
                schema: "res",
                table: "bill",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bill_till_session_id",
                schema: "res",
                table: "bill",
                column: "till_session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bill_till_session_id",
                schema: "res",
                table: "bill");

            migrationBuilder.DropColumn(
                name: "cancel_authorised_by_user_id",
                schema: "res",
                table: "order");

            migrationBuilder.DropColumn(
                name: "cash_amount",
                schema: "res",
                table: "bill");

            migrationBuilder.DropColumn(
                name: "reversal_authorised_by_user_id",
                schema: "res",
                table: "bill");

            migrationBuilder.DropColumn(
                name: "till_session_id",
                schema: "res",
                table: "bill");
        }
    }
}
