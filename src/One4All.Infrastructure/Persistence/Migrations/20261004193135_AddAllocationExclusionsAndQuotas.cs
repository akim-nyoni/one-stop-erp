using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationExclusionsAndQuotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "declared_groups",
                schema: "hse",
                table: "applicant",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "allocation_exclusion",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    allocation_run_id = table.Column<long>(type: "bigint", nullable: false),
                    applicant_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_exclusion", x => x.id);
                    table.ForeignKey(
                        name: "fk_allocation_exclusion_allocation_run_allocation_run_id",
                        column: x => x.allocation_run_id,
                        principalSchema: "hse",
                        principalTable: "allocation_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_allocation_exclusion_housing_applicants_applicant_id",
                        column: x => x.applicant_id,
                        principalSchema: "hse",
                        principalTable: "applicant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "allocation_quota",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: true),
                    group = table.Column<int>(type: "integer", nullable: false),
                    target_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_quota", x => x.id);
                    table.ForeignKey(
                        name: "fk_allocation_quota_housing_schemes_scheme_id",
                        column: x => x.scheme_id,
                        principalSchema: "hse",
                        principalTable: "scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_exclusion_allocation_run_id",
                schema: "hse",
                table: "allocation_exclusion",
                column: "allocation_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_exclusion_applicant_id",
                schema: "hse",
                table: "allocation_exclusion",
                column: "applicant_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_quota_company_id_group",
                schema: "hse",
                table: "allocation_quota",
                columns: new[] { "company_id", "group" },
                unique: true,
                filter: "scheme_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_quota_company_id_scheme_id_group",
                schema: "hse",
                table: "allocation_quota",
                columns: new[] { "company_id", "scheme_id", "group" },
                unique: true,
                filter: "scheme_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_quota_scheme_id",
                schema: "hse",
                table: "allocation_quota",
                column: "scheme_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocation_exclusion",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "allocation_quota",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "declared_groups",
                schema: "hse",
                table: "applicant");
        }
    }
}
