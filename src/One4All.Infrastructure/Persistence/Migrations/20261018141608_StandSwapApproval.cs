using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StandSwapApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "approved_by_user_id",
                schema: "hse",
                table: "stand_swap",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "hse",
                table: "stand_swap",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "requested_by_user_id",
                schema: "hse",
                table: "stand_swap",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "status",
                schema: "hse",
                table: "stand_swap",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Swaps recorded before this change took effect at once: they are approved, and whoever approved them asked for them.
            migrationBuilder.Sql("UPDATE hse.stand_swap SET status = 1, requested_by_user_id = approved_by_user_id WHERE approved_by_user_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "hse",
                table: "stand_swap");

            migrationBuilder.DropColumn(
                name: "requested_by_user_id",
                schema: "hse",
                table: "stand_swap");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "hse",
                table: "stand_swap");

            migrationBuilder.AlterColumn<long>(
                name: "approved_by_user_id",
                schema: "hse",
                table: "stand_swap",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
