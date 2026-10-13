using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaveGradeAndCover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "grade_days",
                schema: "pay",
                table: "leave_policy",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_away_per_department",
                schema: "pay",
                table: "leave_policy",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "grade_days",
                schema: "pay",
                table: "leave_policy");

            migrationBuilder.DropColumn(
                name: "max_away_per_department",
                schema: "pay",
                table: "leave_policy");
        }
    }
}
