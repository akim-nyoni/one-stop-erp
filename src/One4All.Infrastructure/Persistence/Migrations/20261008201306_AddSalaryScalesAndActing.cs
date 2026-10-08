using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryScalesAndActing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "last_increment_on",
                schema: "hr",
                table: "employee",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "acting_appointment",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    grade = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    notch = table.Column<int>(type: "integer", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acting_appointment", x => x.id);
                    table.ForeignKey(
                        name: "fk_acting_appointment_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "salary_scale",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    grade = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    notch = table.Column<int>(type: "integer", nullable: false),
                    monthly_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_scale", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acting_appointment_company_id_employee_id",
                schema: "hr",
                table: "acting_appointment",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_acting_appointment_employee_id",
                schema: "hr",
                table: "acting_appointment",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_scale_company_id_grade_notch_effective_from",
                schema: "hr",
                table: "salary_scale",
                columns: new[] { "company_id", "grade", "notch", "effective_from" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acting_appointment",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "salary_scale",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "last_increment_on",
                schema: "hr",
                table: "employee");
        }
    }
}
