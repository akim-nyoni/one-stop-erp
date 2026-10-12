using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminalProfilesAndCashierFavourites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_favourite_company_id_terminal_id_item_id",
                schema: "pos",
                table: "favourite");

            migrationBuilder.AddColumn<int>(
                name: "profile",
                schema: "pos",
                table: "terminal",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "colour",
                schema: "pos",
                table: "favourite",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page",
                schema: "pos",
                table: "favourite",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "user_id",
                schema: "pos",
                table: "favourite",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_favourite_company_id_terminal_id_user_id_item_id",
                schema: "pos",
                table: "favourite",
                columns: new[] { "company_id", "terminal_id", "user_id", "item_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_favourite_company_id_terminal_id_user_id_item_id",
                schema: "pos",
                table: "favourite");

            migrationBuilder.DropColumn(
                name: "profile",
                schema: "pos",
                table: "terminal");

            migrationBuilder.DropColumn(
                name: "colour",
                schema: "pos",
                table: "favourite");

            migrationBuilder.DropColumn(
                name: "page",
                schema: "pos",
                table: "favourite");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "pos",
                table: "favourite");

            migrationBuilder.CreateIndex(
                name: "ix_favourite_company_id_terminal_id_item_id",
                schema: "pos",
                table: "favourite",
                columns: new[] { "company_id", "terminal_id", "item_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
