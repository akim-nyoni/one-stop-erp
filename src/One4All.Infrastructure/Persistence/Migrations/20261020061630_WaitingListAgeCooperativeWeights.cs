using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WaitingListAgeCooperativeWeights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "age_points_from_years",
                schema: "hse",
                table: "waiting_list_policy",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "cooperative_member_points",
                schema: "hse",
                table: "waiting_list_policy",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "points_per_year_of_age",
                schema: "hse",
                table: "waiting_list_policy",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "age_points_from_years",
                schema: "hse",
                table: "waiting_list_policy");

            migrationBuilder.DropColumn(
                name: "cooperative_member_points",
                schema: "hse",
                table: "waiting_list_policy");

            migrationBuilder.DropColumn(
                name: "points_per_year_of_age",
                schema: "hse",
                table: "waiting_list_policy");
        }
    }
}
