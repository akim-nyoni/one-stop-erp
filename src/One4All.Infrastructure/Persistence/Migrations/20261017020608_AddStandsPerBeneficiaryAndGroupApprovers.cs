using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStandsPerBeneficiaryAndGroupApprovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "required_group_name",
                schema: "wf",
                table: "stage",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stands_per_beneficiary",
                schema: "hse",
                table: "scheme",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stands_per_beneficiary",
                schema: "sys",
                table: "company",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "required_group_name",
                schema: "wf",
                table: "stage");

            migrationBuilder.DropColumn(
                name: "stands_per_beneficiary",
                schema: "hse",
                table: "scheme");

            migrationBuilder.DropColumn(
                name: "stands_per_beneficiary",
                schema: "sys",
                table: "company");
        }
    }
}
