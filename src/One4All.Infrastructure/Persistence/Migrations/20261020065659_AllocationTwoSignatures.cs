using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllocationTwoSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "approved_by_user_id",
                schema: "hse",
                table: "enumerated_household",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "requested_by_user_id",
                schema: "hse",
                table: "enumerated_household",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "approved_by_user_id",
                schema: "hse",
                table: "cooperative_stand",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "requested_by_user_id",
                schema: "hse",
                table: "cooperative_stand",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                schema: "hse",
                table: "enumerated_household");

            migrationBuilder.DropColumn(
                name: "requested_by_user_id",
                schema: "hse",
                table: "enumerated_household");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                schema: "hse",
                table: "cooperative_stand");

            migrationBuilder.DropColumn(
                name: "requested_by_user_id",
                schema: "hse",
                table: "cooperative_stand");
        }
    }
}
