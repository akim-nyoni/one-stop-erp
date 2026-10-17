using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeterReadingBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "billed_on",
                schema: "mun",
                table: "meter_reading",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "billing_run_id",
                schema: "mun",
                table: "meter_reading",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "consumption",
                schema: "mun",
                table: "billing_run_line",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "reading_ids",
                schema: "mun",
                table: "billing_run_line",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billed_on",
                schema: "mun",
                table: "meter_reading");

            migrationBuilder.DropColumn(
                name: "billing_run_id",
                schema: "mun",
                table: "meter_reading");

            migrationBuilder.DropColumn(
                name: "consumption",
                schema: "mun",
                table: "billing_run_line");

            migrationBuilder.DropColumn(
                name: "reading_ids",
                schema: "mun",
                table: "billing_run_line");
        }
    }
}
