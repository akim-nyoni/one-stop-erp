using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoidDetailsAndPlannedMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refurbishment_job_number",
                schema: "hse",
                table: "rental_unit",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "relet_on",
                schema: "hse",
                table: "rental_unit",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "void_reason",
                schema: "hse",
                table: "rental_unit",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "planned_maintenance",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    next_due = table.Column<DateOnly>(type: "date", nullable: false),
                    every_months = table.Column<int>(type: "integer", nullable: false),
                    last_done = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planned_maintenance", x => x.id);
                    table.CheckConstraint("ck_planned_maintenance_every", "every_months >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_planned_maintenance_company_id_next_due",
                schema: "hse",
                table: "planned_maintenance",
                columns: new[] { "company_id", "next_due" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "planned_maintenance",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "refurbishment_job_number",
                schema: "hse",
                table: "rental_unit");

            migrationBuilder.DropColumn(
                name: "relet_on",
                schema: "hse",
                table: "rental_unit");

            migrationBuilder.DropColumn(
                name: "void_reason",
                schema: "hse",
                table: "rental_unit");
        }
    }
}
