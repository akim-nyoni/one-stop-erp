using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationAppeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stand_allocation_certificate_number",
                schema: "hse",
                table: "stand_allocation");

            migrationBuilder.AddColumn<long>(
                name: "company_id",
                schema: "hse",
                table: "stand_allocation",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing allocations take the council of the run they belong to.
            migrationBuilder.Sql("UPDATE hse.stand_allocation sa SET company_id = r.company_id FROM hse.allocation_run r WHERE sa.allocation_run_id = r.id;");

            migrationBuilder.CreateTable(
                name: "allocation_appeal",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    appeal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    applicant_id = table.Column<long>(type: "bigint", nullable: false),
                    allocation_run_id = table.Column<long>(type: "bigint", nullable: false),
                    grounds = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    lodged_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    hearing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    outcome_reasons = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reallocation_certificate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_appeal", x => x.id);
                    table.ForeignKey(
                        name: "fk_allocation_appeal_allocation_run_allocation_run_id",
                        column: x => x.allocation_run_id,
                        principalSchema: "hse",
                        principalTable: "allocation_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocation_appeal_housing_applicants_applicant_id",
                        column: x => x.applicant_id,
                        principalSchema: "hse",
                        principalTable: "applicant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stand_allocation_company_id_certificate_number",
                schema: "hse",
                table: "stand_allocation",
                columns: new[] { "company_id", "certificate_number" },
                unique: true,
                filter: "certificate_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_appeal_allocation_run_id",
                schema: "hse",
                table: "allocation_appeal",
                column: "allocation_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_appeal_applicant_id_allocation_run_id",
                schema: "hse",
                table: "allocation_appeal",
                columns: new[] { "applicant_id", "allocation_run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_allocation_appeal_company_id_appeal_number",
                schema: "hse",
                table: "allocation_appeal",
                columns: new[] { "company_id", "appeal_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocation_appeal",
                schema: "hse");

            migrationBuilder.DropIndex(
                name: "ix_stand_allocation_company_id_certificate_number",
                schema: "hse",
                table: "stand_allocation");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "hse",
                table: "stand_allocation");

            migrationBuilder.CreateIndex(
                name: "ix_stand_allocation_certificate_number",
                schema: "hse",
                table: "stand_allocation",
                column: "certificate_number",
                unique: true,
                filter: "certificate_number IS NOT NULL");
        }
    }
}
