using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportSubscriptionOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "owner_user_id",
                schema: "rpt",
                table: "report_subscription",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_subscription_owner_user_id",
                schema: "rpt",
                table: "report_subscription",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_report_subscription_owner_user_id",
                schema: "rpt",
                table: "report_subscription");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                schema: "rpt",
                table: "report_subscription");
        }
    }
}
