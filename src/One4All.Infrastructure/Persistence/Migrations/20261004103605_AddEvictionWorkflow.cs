using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvictionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eviction_case",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    case_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tenancy_id = table.Column<long>(type: "bigint", nullable: false),
                    arrears_at_opening = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notice_period_days = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    withdrawn_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eviction_case", x => x.id);
                    table.CheckConstraint("ck_eviction_case_notice", "notice_period_days >= 1");
                    table.ForeignKey(
                        name: "fk_eviction_case_tenancy_tenancy_id",
                        column: x => x.tenancy_id,
                        principalSchema: "hse",
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eviction_step",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    eviction_case_id = table.Column<long>(type: "bigint", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eviction_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_eviction_step_eviction_case_eviction_case_id",
                        column: x => x.eviction_case_id,
                        principalSchema: "hse",
                        principalTable: "eviction_case",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eviction_case_company_id_case_number",
                schema: "hse",
                table: "eviction_case",
                columns: new[] { "company_id", "case_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_eviction_case_one_open_per_tenancy",
                schema: "hse",
                table: "eviction_case",
                column: "tenancy_id",
                unique: true,
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_eviction_step_eviction_case_id_stage",
                schema: "hse",
                table: "eviction_step",
                columns: new[] { "eviction_case_id", "stage" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eviction_step",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "eviction_case",
                schema: "hse");
        }
    }
}
