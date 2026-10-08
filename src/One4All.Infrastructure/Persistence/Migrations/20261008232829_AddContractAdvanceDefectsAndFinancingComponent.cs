using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractAdvanceDefectsAndFinancingComponent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "advance_recovered",
                schema: "job",
                table: "progress_certificate",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "variations_to_date",
                schema: "job",
                table: "progress_certificate",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "financing_earned",
                schema: "hse",
                table: "instalment_schedule_line",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "financing_unwind",
                schema: "hse",
                table: "instalment_schedule_line",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "deferred_financing_account_code",
                schema: "hse",
                table: "instalment_agreement",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "financing_discount",
                schema: "hse",
                table: "instalment_agreement",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "advance_account_code",
                schema: "job",
                table: "contract",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "advance_received",
                schema: "job",
                table: "contract",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "advance_recovered",
                schema: "job",
                table: "contract",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "advance_recovery_percent",
                schema: "job",
                table: "contract",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "defects_liability_months",
                schema: "job",
                table: "contract",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "practical_completion_date",
                schema: "job",
                table: "contract",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "retention_released",
                schema: "job",
                table: "contract",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "housing_market_rate_percent",
                schema: "sys",
                table: "company",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "advance_recovered",
                schema: "job",
                table: "progress_certificate");

            migrationBuilder.DropColumn(
                name: "variations_to_date",
                schema: "job",
                table: "progress_certificate");

            migrationBuilder.DropColumn(
                name: "financing_earned",
                schema: "hse",
                table: "instalment_schedule_line");

            migrationBuilder.DropColumn(
                name: "financing_unwind",
                schema: "hse",
                table: "instalment_schedule_line");

            migrationBuilder.DropColumn(
                name: "deferred_financing_account_code",
                schema: "hse",
                table: "instalment_agreement");

            migrationBuilder.DropColumn(
                name: "financing_discount",
                schema: "hse",
                table: "instalment_agreement");

            migrationBuilder.DropColumn(
                name: "advance_account_code",
                schema: "job",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "advance_received",
                schema: "job",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "advance_recovered",
                schema: "job",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "advance_recovery_percent",
                schema: "job",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "defects_liability_months",
                schema: "job",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "practical_completion_date",
                schema: "job",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "retention_released",
                schema: "job",
                table: "contract");

            migrationBuilder.DropColumn(
                name: "housing_market_rate_percent",
                schema: "sys",
                table: "company");
        }
    }
}
