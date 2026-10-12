using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeMasterDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "job_title",
                schema: "hr",
                table: "employee",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "notice_period_days",
                schema: "hr",
                table: "employee",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "hr",
                table: "employee",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "photo",
                schema: "hr",
                table: "employee",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_content_type",
                schema: "hr",
                table: "employee",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "physical_address",
                schema: "hr",
                table: "employee",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_address",
                schema: "hr",
                table: "employee",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workers_compensation_class",
                schema: "hr",
                table: "employee",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_dependant",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    id_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    share_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    medical_aid_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_dependant", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_dependant_company_id_employee_id",
                schema: "hr",
                table: "employee_dependant",
                columns: new[] { "company_id", "employee_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_dependant",
                schema: "hr");

            migrationBuilder.DropColumn(
                name: "job_title",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "notice_period_days",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "photo",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "photo_content_type",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "physical_address",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "postal_address",
                schema: "hr",
                table: "employee");

            migrationBuilder.DropColumn(
                name: "workers_compensation_class",
                schema: "hr",
                table: "employee");
        }
    }
}
