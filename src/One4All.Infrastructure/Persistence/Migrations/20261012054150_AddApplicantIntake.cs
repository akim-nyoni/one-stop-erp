using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicantIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "employer",
                schema: "hse",
                table: "applicant",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gender",
                schema: "hse",
                table: "applicant",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "hse",
                table: "applicant",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "physical_address",
                schema: "hse",
                table: "applicant",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_address",
                schema: "hse",
                table: "applicant",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "applicant_document",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    applicant_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lodged_on = table.Column<DateOnly>(type: "date", nullable: false),
                    verified_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    verified_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applicant_document", x => x.id);
                    table.ForeignKey(
                        name: "fk_applicant_document_housing_applicants_applicant_id",
                        column: x => x.applicant_id,
                        principalSchema: "hse",
                        principalTable: "applicant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deceased_person",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    id_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    died_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deceased_person", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "duplicate_case",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    first_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    first_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    second_source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    second_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decision_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duplicate_case", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applicant_document_applicant_id",
                schema: "hse",
                table: "applicant_document",
                column: "applicant_id");

            migrationBuilder.CreateIndex(
                name: "ix_deceased_person_company_id_id_number",
                schema: "hse",
                table: "deceased_person",
                columns: new[] { "company_id", "id_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_case_company_id_status",
                schema: "hse",
                table: "duplicate_case",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applicant_document",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "deceased_person",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "duplicate_case",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "employer",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "gender",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "physical_address",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "postal_address",
                schema: "hse",
                table: "applicant");
        }
    }
}
