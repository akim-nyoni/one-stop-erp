using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankReconciliationSignOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "reconciled_by_user_id",
                schema: "cb",
                table: "bank_account",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "reconciled_to",
                schema: "cb",
                table: "bank_account",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reconciled_by_user_id",
                schema: "cb",
                table: "bank_account");

            migrationBuilder.DropColumn(
                name: "reconciled_to",
                schema: "cb",
                table: "bank_account");
        }
    }
}
