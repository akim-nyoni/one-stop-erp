using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierBlacklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "blacklist_reason",
                schema: "ap",
                table: "supplier",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_blacklisted",
                schema: "ap",
                table: "supplier",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "blacklist_reason",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "is_blacklisted",
                schema: "ap",
                table: "supplier");
        }
    }
}
