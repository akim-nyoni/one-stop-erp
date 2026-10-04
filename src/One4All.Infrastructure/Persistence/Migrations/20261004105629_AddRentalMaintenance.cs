using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRentalMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "unit_inspection",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: false),
                    inspected_on = table.Column<DateOnly>(type: "date", nullable: false),
                    inspector = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    condition_grade = table.Column<int>(type: "integer", nullable: false),
                    findings = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    follow_up_required = table.Column<bool>(type: "boolean", nullable: false),
                    follow_up_done_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_inspection", x => x.id);
                    table.CheckConstraint("ck_unit_inspection_grade", "condition_grade BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_unit_inspection_rental_unit_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "hse",
                        principalTable: "rental_unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unit_repair",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    repair_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: false),
                    tenancy_id = table.Column<long>(type: "bigint", nullable: true),
                    reported_on = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    completed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tenant_caused = table.Column<bool>(type: "boolean", nullable: false),
                    recharge_invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_repair", x => x.id);
                    table.CheckConstraint("ck_unit_repair_cost", "cost >= 0");
                    table.ForeignKey(
                        name: "fk_unit_repair_rental_unit_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "hse",
                        principalTable: "rental_unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_unit_inspection_unit_id_inspected_on",
                schema: "hse",
                table: "unit_inspection",
                columns: new[] { "unit_id", "inspected_on" });

            migrationBuilder.CreateIndex(
                name: "ix_unit_repair_company_id_repair_number",
                schema: "hse",
                table: "unit_repair",
                columns: new[] { "company_id", "repair_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_repair_unit_id_status",
                schema: "hse",
                table: "unit_repair",
                columns: new[] { "unit_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "unit_inspection",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "unit_repair",
                schema: "hse");
        }
    }
}
