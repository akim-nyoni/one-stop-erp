using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNetPayProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "suspension_priority",
                schema: "pay",
                table: "element",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_take_home_amount",
                schema: "sys",
                table: "company",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_take_home_percent",
                schema: "sys",
                table: "company",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "deduction_arrear",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    element_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    period_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    run_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: false),
                    attempted = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    deducted = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    movement = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deduction_arrear", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deduction_arrear_company_id_employee_id_element_code",
                schema: "pay",
                table: "deduction_arrear",
                columns: new[] { "company_id", "employee_id", "element_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deduction_arrear",
                schema: "pay");

            migrationBuilder.DropColumn(
                name: "suspension_priority",
                schema: "pay",
                table: "element");

            migrationBuilder.DropColumn(
                name: "minimum_take_home_amount",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "minimum_take_home_percent",
                schema: "sys",
                table: "company");
        }
    }
}
