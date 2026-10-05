using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollStatutory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_run",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    period_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    employees = table.Column<int>(type: "integer", nullable: false),
                    gross = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    deductions = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_run", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "statutory_scheme",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    employee_element_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    employer_element_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statutory_scheme", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_table",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_table", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheme_membership",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    member_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    left_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheme_membership", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheme_membership_employee_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "hr",
                        principalTable: "employee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scheme_membership_statutory_scheme_scheme_id",
                        column: x => x.scheme_id,
                        principalSchema: "hr",
                        principalTable: "statutory_scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheme_remittance",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    period_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheme_remittance", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheme_remittance_statutory_scheme_scheme_id",
                        column: x => x.scheme_id,
                        principalSchema: "hr",
                        principalTable: "statutory_scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_band",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tax_table_id = table.Column<long>(type: "bigint", nullable: false),
                    from = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    to = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    rate_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_band", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_band_tax_table_tax_table_id",
                        column: x => x.tax_table_id,
                        principalSchema: "hr",
                        principalTable: "tax_table",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_company_id_period_label",
                schema: "hr",
                table: "payroll_run",
                columns: new[] { "company_id", "period_label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheme_membership_employee_id",
                schema: "hr",
                table: "scheme_membership",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheme_membership_scheme_id_employee_id",
                schema: "hr",
                table: "scheme_membership",
                columns: new[] { "scheme_id", "employee_id" },
                unique: true,
                filter: "left_on IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scheme_remittance_scheme_id_period_label",
                schema: "hr",
                table: "scheme_remittance",
                columns: new[] { "scheme_id", "period_label" });

            migrationBuilder.CreateIndex(
                name: "ix_statutory_scheme_company_id_code",
                schema: "hr",
                table: "statutory_scheme",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_band_tax_table_id",
                schema: "hr",
                table: "tax_band",
                column: "tax_table_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_table_company_id_code_effective_from",
                schema: "hr",
                table: "tax_table",
                columns: new[] { "company_id", "code", "effective_from" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_run",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "scheme_membership",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "scheme_remittance",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "tax_band",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "statutory_scheme",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "tax_table",
                schema: "hr");
        }
    }
}
