using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolFeeControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "disclosure_note",
                schema: "sch",
                table: "student",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "do_not_disclose",
                schema: "sch",
                table: "student",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "staff_employee_id",
                schema: "sch",
                table: "student",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "staff_child_discount_percent",
                schema: "sys",
                table: "company",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "bursary_award",
                schema: "sch",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    student_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bursary_award", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bursary_award_company_id_reference",
                schema: "sch",
                table: "bursary_award",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bursary_award_student_id",
                schema: "sch",
                table: "bursary_award",
                column: "student_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bursary_award",
                schema: "sch");

            migrationBuilder.DropColumn(
                name: "disclosure_note",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "do_not_disclose",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "staff_employee_id",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "staff_child_discount_percent",
                schema: "sys",
                table: "company");
        }
    }
}
