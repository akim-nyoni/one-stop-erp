using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBursaryExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "bursary_valid_until",
                schema: "sch",
                table: "student",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "valid_until",
                schema: "sch",
                table: "bursary_award",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bursary_valid_until",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "valid_until",
                schema: "sch",
                table: "bursary_award");
        }
    }
}
