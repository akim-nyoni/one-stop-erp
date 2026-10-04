using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_job",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    task_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    frequency = table.Column<int>(type: "integer", nullable: false),
                    time_of_day = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: true),
                    day_of_month = table.Column<int>(type: "integer", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_succeeded = table.Column<bool>(type: "boolean", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_job", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_run",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    scheduled_job_id = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_run", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_run_scheduled_job_scheduled_job_id",
                        column: x => x.scheduled_job_id,
                        principalSchema: "sys",
                        principalTable: "scheduled_job",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_run_scheduled_job_id",
                schema: "sys",
                table: "job_run",
                column: "scheduled_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_company_id_code",
                schema: "sys",
                table: "scheduled_job",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_job_is_enabled_next_run_at",
                schema: "sys",
                table: "scheduled_job",
                columns: new[] { "is_enabled", "next_run_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_run",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "scheduled_job",
                schema: "sys");
        }
    }
}
