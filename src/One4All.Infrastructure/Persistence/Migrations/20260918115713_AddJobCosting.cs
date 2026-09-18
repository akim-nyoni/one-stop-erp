using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "job");

            migrationBuilder.CreateTable(
                name: "contract",
                schema: "job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    contract_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    contract_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    budget_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    retention_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    certified_to_date = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    retention_held = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "progress_certificate",
                schema: "job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    job_contract_id = table.Column<long>(type: "bigint", nullable: false),
                    certificate_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    work_done_to_date = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    materials_on_site = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    previously_certified = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    gross_this_certificate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    retention_this_certificate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net_payable = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_progress_certificate", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "weighbridge_ticket",
                schema: "job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    ticket_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    vehicle_registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    product = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    gross_weight_kg = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tare_weight_kg = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_complete = table.Column<bool>(type: "boolean", nullable: false),
                    deduction_kg = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    cubic_metres_per_tonne = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weighbridge_ticket", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_entry",
                schema: "job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    job_contract_id = table.Column<long>(type: "bigint", nullable: false),
                    cost_head = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_entry_contract_job_contract_id",
                        column: x => x.job_contract_id,
                        principalSchema: "job",
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_company_id_contract_number",
                schema: "job",
                table: "contract",
                columns: new[] { "company_id", "contract_number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_cost_entry_company_id_job_contract_id",
                schema: "job",
                table: "cost_entry",
                columns: new[] { "company_id", "job_contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cost_entry_job_contract_id",
                schema: "job",
                table: "cost_entry",
                column: "job_contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_progress_certificate_company_id_certificate_number",
                schema: "job",
                table: "progress_certificate",
                columns: new[] { "company_id", "certificate_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_weighbridge_ticket_company_id_ticket_number",
                schema: "job",
                table: "weighbridge_ticket",
                columns: new[] { "company_id", "ticket_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cost_entry",
                schema: "job");

            migrationBuilder.DropTable(
                name: "progress_certificate",
                schema: "job");

            migrationBuilder.DropTable(
                name: "weighbridge_ticket",
                schema: "job");

            migrationBuilder.DropTable(
                name: "contract",
                schema: "job");
        }
    }
}
