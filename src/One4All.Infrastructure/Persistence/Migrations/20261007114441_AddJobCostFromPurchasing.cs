using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCostFromPurchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "job_contract_id",
                schema: "pop",
                table: "purchase_order_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_cost_head",
                schema: "pop",
                table: "purchase_order_line",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "overhead_rate",
                schema: "job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    applies_to = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    cost_head = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overhead_rate", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_overhead_rate_company_id_applies_to",
                schema: "job",
                table: "overhead_rate",
                columns: new[] { "company_id", "applies_to" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "overhead_rate",
                schema: "job");

            migrationBuilder.DropColumn(
                name: "job_contract_id",
                schema: "pop",
                table: "purchase_order_line");

            migrationBuilder.DropColumn(
                name: "job_cost_head",
                schema: "pop",
                table: "purchase_order_line");
        }
    }
}
