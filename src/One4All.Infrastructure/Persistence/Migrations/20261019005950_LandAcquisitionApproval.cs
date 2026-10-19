using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LandAcquisitionApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "approval",
                schema: "hse",
                table: "land_acquisition",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approval",
                schema: "hse",
                table: "land_acquisition");
        }
    }
}
