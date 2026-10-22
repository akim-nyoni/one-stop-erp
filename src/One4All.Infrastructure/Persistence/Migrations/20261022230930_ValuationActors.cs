using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ValuationActors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "decided_by_user_id",
                schema: "mun",
                table: "valuation_objection",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "recorded_by_user_id",
                schema: "mun",
                table: "property_valuation",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "valuation_backdate_months",
                schema: "sys",
                table: "company",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "valuation_credit_approval_threshold",
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
                name: "decided_by_user_id",
                schema: "mun",
                table: "valuation_objection");

            migrationBuilder.DropColumn(
                name: "recorded_by_user_id",
                schema: "mun",
                table: "property_valuation");

            migrationBuilder.DropColumn(
                name: "valuation_backdate_months",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "valuation_credit_approval_threshold",
                schema: "sys",
                table: "company");
        }
    }
}
