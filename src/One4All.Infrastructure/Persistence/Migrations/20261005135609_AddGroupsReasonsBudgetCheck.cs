using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupsReasonsBudgetCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "expense_account_code",
                schema: "pop",
                table: "requisition_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                schema: "trd",
                table: "open_item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "parent_customer_id",
                schema: "ar",
                table: "customer",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expense_account_code",
                schema: "pop",
                table: "requisition_line");

            migrationBuilder.DropColumn(
                name: "reason_code",
                schema: "trd",
                table: "open_item");

            migrationBuilder.DropColumn(
                name: "parent_customer_id",
                schema: "ar",
                table: "customer");
        }
    }
}
