using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobWbs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cost_head",
                schema: "job",
                table: "subcontract",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Subcontract");

            migrationBuilder.CreateTable(
                name: "wbs_element",
                schema: "job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    job_contract_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    budget_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    cost_head = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    estimate_to_complete = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    estimated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wbs_element", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wbs_element_job_contract_id_code",
                schema: "job",
                table: "wbs_element",
                columns: new[] { "job_contract_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wbs_element",
                schema: "job");

            migrationBuilder.DropColumn(
                name: "cost_head",
                schema: "job",
                table: "subcontract");
        }
    }
}
