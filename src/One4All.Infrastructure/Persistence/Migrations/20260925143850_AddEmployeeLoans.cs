using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_loan",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    principal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    installment = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    granted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    loan_receivable_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    outstanding = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_loan", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_loan_company_id_employee_id_status",
                schema: "pay",
                table: "employee_loan",
                columns: new[] { "company_id", "employee_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_loan",
                schema: "pay");
        }
    }
}
