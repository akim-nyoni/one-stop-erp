using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DisassemblyAndRecounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "counted_by_user_id",
                schema: "inv",
                table: "stocktake_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "first_count_quantity",
                schema: "inv",
                table: "stocktake_line",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "recounted_by_user_id",
                schema: "inv",
                table: "stocktake_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "recount_tolerance_percent",
                schema: "inv",
                table: "stocktake",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_disassembly",
                schema: "inv",
                table: "assembly_run",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "counted_by_user_id",
                schema: "inv",
                table: "stocktake_line");

            migrationBuilder.DropColumn(
                name: "first_count_quantity",
                schema: "inv",
                table: "stocktake_line");

            migrationBuilder.DropColumn(
                name: "recounted_by_user_id",
                schema: "inv",
                table: "stocktake_line");

            migrationBuilder.DropColumn(
                name: "recount_tolerance_percent",
                schema: "inv",
                table: "stocktake");

            migrationBuilder.DropColumn(
                name: "is_disassembly",
                schema: "inv",
                table: "assembly_run");
        }
    }
}
