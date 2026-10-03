using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitmentYearEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "carried_from_id",
                schema: "bud",
                table: "commitment",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "year_end_approved_by_user_id",
                schema: "bud",
                table: "commitment",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "carried_from_id",
                schema: "bud",
                table: "commitment");

            migrationBuilder.DropColumn(
                name: "year_end_approved_by_user_id",
                schema: "bud",
                table: "commitment");
        }
    }
}
