using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStandAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "allocation_run",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    run_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    seed = table.Column<long>(type: "bigint", nullable: true),
                    input_candidates = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    input_stands = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    proposed_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_run", x => x.id);
                    table.CheckConstraint("ck_allocation_run_approver", "approved_by_user_id IS NULL OR approved_by_user_id <> proposed_by_user_id");
                });

            migrationBuilder.CreateTable(
                name: "stand_allocation",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    allocation_run_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    applicant_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    certificate_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    offer_expires = table.Column<DateOnly>(type: "date", nullable: true),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stand_allocation", x => x.id);
                    table.ForeignKey(
                        name: "fk_stand_allocation_allocation_run_allocation_run_id",
                        column: x => x.allocation_run_id,
                        principalSchema: "hse",
                        principalTable: "allocation_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stand_allocation_applicant_applicant_id",
                        column: x => x.applicant_id,
                        principalSchema: "hse",
                        principalTable: "applicant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stand_allocation_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_run_company_id_run_number",
                schema: "hse",
                table: "allocation_run",
                columns: new[] { "company_id", "run_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stand_allocation_allocation_run_id",
                schema: "hse",
                table: "stand_allocation",
                column: "allocation_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_stand_allocation_certificate_number",
                schema: "hse",
                table: "stand_allocation",
                column: "certificate_number",
                unique: true,
                filter: "certificate_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stand_allocation_one_live_per_applicant",
                schema: "hse",
                table: "stand_allocation",
                column: "applicant_id",
                unique: true,
                filter: "status IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_stand_allocation_one_live_per_stand",
                schema: "hse",
                table: "stand_allocation",
                column: "stand_id",
                unique: true,
                filter: "status IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stand_allocation",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "allocation_run",
                schema: "hse");
        }
    }
}
