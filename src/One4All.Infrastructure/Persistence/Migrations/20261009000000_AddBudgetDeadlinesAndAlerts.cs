using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetDeadlinesAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "submission_deadline",
                schema: "bud",
                table: "department_unit",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "budget_alert_email",
                schema: "sys",
                table: "company",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "budget_alert_thresholds",
                schema: "sys",
                table: "company",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alert_sent",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    fiscal_year_id = table.Column<long>(type: "bigint", nullable: false),
                    account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    threshold = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    sent_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_sent", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_sent_company_id_fiscal_year_id_account_code_threshold",
                schema: "bud",
                table: "alert_sent",
                columns: new[] { "company_id", "fiscal_year_id", "account_code", "threshold" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_sent",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "submission_deadline",
                schema: "bud",
                table: "department_unit");

            migrationBuilder.DropColumn(
                name: "budget_alert_email",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "budget_alert_thresholds",
                schema: "sys",
                table: "company");
        }
    }
}
