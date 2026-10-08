using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBenefitsInKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "interest_rate_percent",
                schema: "pay",
                table: "employee_loan",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "deemed_loan_interest_rate_percent",
                schema: "sys",
                table: "company",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_benefit",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    engine_capacity_cc = table.Column<int>(type: "integer", nullable: true),
                    cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    monthly_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_benefit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_benefit_scale",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    from = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    to = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    monthly_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_benefit_scale", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_benefit_company_id_employee_id",
                schema: "pay",
                table: "employee_benefit",
                columns: new[] { "company_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_benefit_scale_company_id_basis_effective_from",
                schema: "pay",
                table: "vehicle_benefit_scale",
                columns: new[] { "company_id", "basis", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_benefit",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "vehicle_benefit_scale",
                schema: "pay");

            migrationBuilder.DropColumn(
                name: "interest_rate_percent",
                schema: "pay",
                table: "employee_loan");

            migrationBuilder.DropColumn(
                name: "deemed_loan_interest_rate_percent",
                schema: "sys",
                table: "company");
        }
    }
}
