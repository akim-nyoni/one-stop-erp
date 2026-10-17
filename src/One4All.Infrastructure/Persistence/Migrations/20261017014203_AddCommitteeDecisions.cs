using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommitteeDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "committee_members",
                schema: "hse",
                table: "allocation_run",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "meeting_date",
                schema: "hse",
                table: "allocation_run",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                schema: "hse",
                table: "allocation_exclusion",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "committee_members",
                schema: "hse",
                table: "allocation_run");

            migrationBuilder.DropColumn(
                name: "meeting_date",
                schema: "hse",
                table: "allocation_run");

            migrationBuilder.DropColumn(
                name: "note",
                schema: "hse",
                table: "allocation_exclusion");
        }
    }
}
