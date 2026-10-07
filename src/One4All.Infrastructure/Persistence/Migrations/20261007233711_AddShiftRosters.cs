using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftRosters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    grace_minutes = table.Column<int>(type: "integer", nullable: false),
                    allowance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_pattern",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    steps = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_pattern", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roster_entry",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_id = table.Column<long>(type: "bigint", nullable: true),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roster_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_roster_entry_shift_shift_id",
                        column: x => x.shift_id,
                        principalSchema: "hr",
                        principalTable: "shift",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roster_assignment",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    shift_pattern_id = table.Column<long>(type: "bigint", nullable: false),
                    anchor = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roster_assignment", x => x.id);
                    table.ForeignKey(
                        name: "fk_roster_assignment_shift_pattern_shift_pattern_id",
                        column: x => x.shift_pattern_id,
                        principalSchema: "hr",
                        principalTable: "shift_pattern",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_roster_assignment_employee_id",
                schema: "hr",
                table: "roster_assignment",
                column: "employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roster_assignment_shift_pattern_id",
                schema: "hr",
                table: "roster_assignment",
                column: "shift_pattern_id");

            migrationBuilder.CreateIndex(
                name: "ix_roster_entry_employee_id_date",
                schema: "hr",
                table: "roster_entry",
                columns: new[] { "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roster_entry_shift_id",
                schema: "hr",
                table: "roster_entry",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_company_id_code",
                schema: "hr",
                table: "shift",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_pattern_company_id_code",
                schema: "hr",
                table: "shift_pattern",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roster_assignment",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "roster_entry",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "shift_pattern",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "shift",
                schema: "hr");
        }
    }
}
