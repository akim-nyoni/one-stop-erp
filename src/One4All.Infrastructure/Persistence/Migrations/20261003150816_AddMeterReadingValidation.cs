using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeterReadingValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "accepted",
                schema: "mun",
                table: "meter_reading",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "billed_consumption",
                schema: "mun",
                table: "meter_reading",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "flags",
                schema: "mun",
                table: "meter_reading",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "kind",
                schema: "mun",
                table: "meter_reading",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "digits",
                schema: "mun",
                table: "meter",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_since_actual",
                schema: "mun",
                table: "meter",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "installed_on",
                schema: "mun",
                table: "meter",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_actual_date",
                schema: "mun",
                table: "meter",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "last_actual_reading",
                schema: "mun",
                table: "meter",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_reading_date",
                schema: "mun",
                table: "meter",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "removed_on",
                schema: "mun",
                table: "meter",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "units_credit",
                schema: "mun",
                table: "meter",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // Existing readings were actual, accepted and billed as recorded; existing meters were last read actually.
            migrationBuilder.Sql("UPDATE mun.meter_reading SET billed_consumption = consumption;");
            migrationBuilder.Sql("UPDATE mun.meter SET last_actual_reading = last_reading;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accepted",
                schema: "mun",
                table: "meter_reading");

            migrationBuilder.DropColumn(
                name: "billed_consumption",
                schema: "mun",
                table: "meter_reading");

            migrationBuilder.DropColumn(
                name: "flags",
                schema: "mun",
                table: "meter_reading");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "mun",
                table: "meter_reading");

            migrationBuilder.DropColumn(
                name: "digits",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "estimated_since_actual",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "installed_on",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "last_actual_date",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "last_actual_reading",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "last_reading_date",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "removed_on",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "units_credit",
                schema: "mun",
                table: "meter");
        }
    }
}
