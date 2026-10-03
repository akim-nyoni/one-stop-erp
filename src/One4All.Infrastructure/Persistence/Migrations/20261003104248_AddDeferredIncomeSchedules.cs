using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeferredIncomeSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "deferred_income_account_code",
                schema: "sch",
                table: "fee_structure",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "term_end",
                schema: "sch",
                table: "fee_structure",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "term_start",
                schema: "sch",
                table: "fee_structure",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "deferred_income_schedule",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    deferred_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recognised_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deferred_income_schedule", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deferred_income_schedule_company_id_start_date",
                schema: "gl",
                table: "deferred_income_schedule",
                columns: new[] { "company_id", "start_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deferred_income_schedule",
                schema: "gl");

            migrationBuilder.DropColumn(
                name: "deferred_income_account_code",
                schema: "sch",
                table: "fee_structure");

            migrationBuilder.DropColumn(
                name: "term_end",
                schema: "sch",
                table: "fee_structure");

            migrationBuilder.DropColumn(
                name: "term_start",
                schema: "sch",
                table: "fee_structure");
        }
    }
}
