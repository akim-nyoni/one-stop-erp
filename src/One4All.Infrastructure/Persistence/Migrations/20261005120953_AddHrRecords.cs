using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHrRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appraisal",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    next_due = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appraisal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "disciplinary",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    offence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sanction = table.Column<int>(type: "integer", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    issued_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disciplinary", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_document",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_document", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "establishment_post",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    department = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    grade = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    approved_posts = table.Column<int>(type: "integer", nullable: false),
                    annual_cost_per_post = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishment_post", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "garnishee_order",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    creditor = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    instalment = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    element_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_garnishee_order", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    course = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    completed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    provider = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_training", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appraisal_company_id_employee_id",
                schema: "hr",
                table: "appraisal",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_disciplinary_company_id_employee_id",
                schema: "hr",
                table: "disciplinary",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_document_company_id_employee_id",
                schema: "hr",
                table: "employee_document",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_establishment_post_company_id_department_grade",
                schema: "hr",
                table: "establishment_post",
                columns: new[] { "company_id", "department", "grade" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_garnishee_order_company_id_reference",
                schema: "hr",
                table: "garnishee_order",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_company_id_employee_id",
                schema: "hr",
                table: "training",
                columns: new[] { "company_id", "employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appraisal",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "disciplinary",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_document",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "establishment_post",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "garnishee_order",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "training",
                schema: "hr");
        }
    }
}
