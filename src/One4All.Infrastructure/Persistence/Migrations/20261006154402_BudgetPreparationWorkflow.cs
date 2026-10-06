using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetPreparationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "budget_approval_required",
                schema: "sys",
                table: "company",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "approved_by_user_id",
                schema: "bud",
                table: "budget",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_reference",
                schema: "bud",
                table: "budget",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "round",
                schema: "bud",
                table: "budget",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "submitted_by_user_id",
                schema: "bud",
                table: "budget",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "proposal",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    budget_id = table.Column<long>(type: "bigint", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    cost_centre = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    period = table.Column<int>(type: "integer", nullable: false),
                    requested = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    proposed = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    comment = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proposal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "review_event",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    budget_id = table.Column<long>(type: "bigint", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_proposal_budget_id_round",
                schema: "bud",
                table: "proposal",
                columns: new[] { "budget_id", "round" });

            migrationBuilder.CreateIndex(
                name: "ix_review_event_budget_id",
                schema: "bud",
                table: "review_event",
                column: "budget_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proposal",
                schema: "bud");

            migrationBuilder.DropTable(
                name: "review_event",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "budget_approval_required",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                schema: "bud",
                table: "budget");

            migrationBuilder.DropColumn(
                name: "resolution_reference",
                schema: "bud",
                table: "budget");

            migrationBuilder.DropColumn(
                name: "round",
                schema: "bud",
                table: "budget");

            migrationBuilder.DropColumn(
                name: "submitted_by_user_id",
                schema: "bud",
                table: "budget");
        }
    }
}
