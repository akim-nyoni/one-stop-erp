using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpportunityClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "closed_on",
                schema: "crm",
                table: "opportunity",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "expected_close_on",
                schema: "crm",
                table: "opportunity",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lost_reason",
                schema: "crm",
                table: "opportunity",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "closed_on",
                schema: "crm",
                table: "opportunity");

            migrationBuilder.DropColumn(
                name: "expected_close_on",
                schema: "crm",
                table: "opportunity");

            migrationBuilder.DropColumn(
                name: "lost_reason",
                schema: "crm",
                table: "opportunity");
        }
    }
}
