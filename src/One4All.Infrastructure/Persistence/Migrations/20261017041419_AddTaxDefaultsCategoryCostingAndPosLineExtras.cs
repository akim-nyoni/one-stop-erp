using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxDefaultsCategoryCostingAndPosLineExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "signed_off_by_user_id",
                schema: "pos",
                table: "till_session",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cash_up_sign_off_variance",
                schema: "pos",
                table: "terminal",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_tax_code",
                schema: "ap",
                table: "supplier",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "gift_wrap",
                schema: "pos",
                table: "sale_line",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "pos",
                table: "sale_line",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "serial_number",
                schema: "pos",
                table: "sale_line",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "res",
                table: "order_line",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "res",
                table: "kitchen_ticket_line",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "costing_method",
                schema: "inv",
                table: "item_category",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_tax_code",
                schema: "gl",
                table: "account",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "signed_off_by_user_id",
                schema: "pos",
                table: "till_session");

            migrationBuilder.DropColumn(
                name: "cash_up_sign_off_variance",
                schema: "pos",
                table: "terminal");

            migrationBuilder.DropColumn(
                name: "default_tax_code",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "gift_wrap",
                schema: "pos",
                table: "sale_line");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "pos",
                table: "sale_line");

            migrationBuilder.DropColumn(
                name: "serial_number",
                schema: "pos",
                table: "sale_line");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "res",
                table: "order_line");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "res",
                table: "kitchen_ticket_line");

            migrationBuilder.DropColumn(
                name: "costing_method",
                schema: "inv",
                table: "item_category");

            migrationBuilder.DropColumn(
                name: "default_tax_code",
                schema: "gl",
                table: "account");
        }
    }
}
