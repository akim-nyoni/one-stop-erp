using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStocktakeApprovalHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_adjustment_account_code",
                schema: "inv",
                table: "stocktake",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "pending_posting_date",
                schema: "inv",
                table: "stocktake",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_adjustment_account_code",
                schema: "inv",
                table: "stocktake");

            migrationBuilder.DropColumn(
                name: "pending_posting_date",
                schema: "inv",
                table: "stocktake");
        }
    }
}
