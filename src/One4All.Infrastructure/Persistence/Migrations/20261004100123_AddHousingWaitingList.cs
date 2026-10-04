using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHousingWaitingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_applicant_company_id_id_number",
                schema: "hse",
                table: "applicant");

            migrationBuilder.AddColumn<string>(
                name: "national_id_number",
                schema: "ar",
                table: "customer",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "category",
                schema: "hse",
                table: "applicant",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                schema: "hse",
                table: "applicant",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dependants",
                schema: "hse",
                table: "applicant",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "has_disability",
                schema: "hse",
                table: "applicant",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_income",
                schema: "hse",
                table: "applicant",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "resident_since",
                schema: "hse",
                table: "applicant",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status",
                schema: "hse",
                table: "applicant",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "tenure",
                schema: "hse",
                table: "applicant",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "waiting_list_number",
                schema: "hse",
                table: "applicant",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "waiting_list_policy",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    minimum_age = table.Column<int>(type: "integer", nullable: false),
                    minimum_residency_years = table.Column<int>(type: "integer", nullable: false),
                    maximum_monthly_income = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    points_per_year_on_list = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    points_per_year_resident = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    points_per_dependant = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    disability_points = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    homeless_points = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    lodger_points = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waiting_list_policy", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_company_id_national_id_number",
                schema: "ar",
                table: "customer",
                columns: new[] { "company_id", "national_id_number" });

            migrationBuilder.CreateIndex(
                name: "ix_applicant_company_id_category_status",
                schema: "hse",
                table: "applicant",
                columns: new[] { "company_id", "category", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_applicant_company_id_id_number",
                schema: "hse",
                table: "applicant",
                columns: new[] { "company_id", "id_number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_applicant_company_id_waiting_list_number",
                schema: "hse",
                table: "applicant",
                columns: new[] { "company_id", "waiting_list_number" },
                unique: true,
                filter: "waiting_list_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_waiting_list_policy_company_id",
                schema: "hse",
                table: "waiting_list_policy",
                column: "company_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waiting_list_policy",
                schema: "hse");

            migrationBuilder.DropIndex(
                name: "ix_customer_company_id_national_id_number",
                schema: "ar",
                table: "customer");

            migrationBuilder.DropIndex(
                name: "ix_applicant_company_id_category_status",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropIndex(
                name: "ix_applicant_company_id_id_number",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropIndex(
                name: "ix_applicant_company_id_waiting_list_number",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "national_id_number",
                schema: "ar",
                table: "customer");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "dependants",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "has_disability",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "monthly_income",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "resident_since",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "tenure",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "waiting_list_number",
                schema: "hse",
                table: "applicant");

            migrationBuilder.CreateIndex(
                name: "ix_applicant_company_id_id_number",
                schema: "hse",
                table: "applicant",
                columns: new[] { "company_id", "id_number" });
        }
    }
}
