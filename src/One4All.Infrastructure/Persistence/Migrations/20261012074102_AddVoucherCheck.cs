using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "checked_by_user_id",
                schema: "vou",
                table: "accounting_voucher",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_on_utc",
                schema: "vou",
                table: "accounting_voucher",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checked_by_user_id",
                schema: "vou",
                table: "accounting_voucher");

            migrationBuilder.DropColumn(
                name: "checked_on_utc",
                schema: "vou",
                table: "accounting_voucher");
        }
    }
}
