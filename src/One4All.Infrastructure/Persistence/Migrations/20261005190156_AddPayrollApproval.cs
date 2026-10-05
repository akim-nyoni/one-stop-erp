using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "payroll_approval_required",
                schema: "sys",
                table: "company",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "payroll_second_approval_variance_percent",
                schema: "sys",
                table: "company",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_draft",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    pay_group = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    period_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    net_pay_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    employees = table.Column<int>(type: "integer", nullable: false),
                    gross = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    deductions = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    previous_net = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    requires_second_approval = table.Column<bool>(type: "boolean", nullable: false),
                    exceptions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    calculated_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    calculated_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_approver_user_id = table.Column<long>(type: "bigint", nullable: true),
                    second_approver_user_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    committed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    committed_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_draft", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_draft_company_id_pay_group_period_label",
                schema: "pay",
                table: "payroll_draft",
                columns: new[] { "company_id", "pay_group", "period_label" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_draft",
                schema: "pay");

            migrationBuilder.DropColumn(
                name: "payroll_approval_required",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "payroll_second_approval_variance_percent",
                schema: "sys",
                table: "company");
        }
    }
}
