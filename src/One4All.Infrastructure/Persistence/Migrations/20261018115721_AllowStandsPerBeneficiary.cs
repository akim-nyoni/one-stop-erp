using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowStandsPerBeneficiary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stand_allocation_one_live_per_applicant",
                schema: "hse",
                table: "stand_allocation");

            migrationBuilder.CreateIndex(
                name: "ix_stand_allocation_live_per_applicant",
                schema: "hse",
                table: "stand_allocation",
                column: "applicant_id",
                filter: "status IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stand_allocation_live_per_applicant",
                schema: "hse",
                table: "stand_allocation");

            migrationBuilder.CreateIndex(
                name: "ix_stand_allocation_one_live_per_applicant",
                schema: "hse",
                table: "stand_allocation",
                column: "applicant_id",
                unique: true,
                filter: "status IN (0, 1, 2)");
        }
    }
}
