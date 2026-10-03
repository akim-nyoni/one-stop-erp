using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "debt_case",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    case_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    handed_over_to = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    handed_over_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_handed_over = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    summons_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    court = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    summons_issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    hearing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    write_off_requested_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    write_off_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    write_off_approved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_case", x => x.id);
                    table.CheckConstraint("ck_debt_case_write_off_approver", "write_off_approved_by_user_id IS NULL OR write_off_approved_by_user_id <> write_off_requested_by_user_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_debt_case_company_id_case_number",
                schema: "ar",
                table: "debt_case",
                columns: new[] { "company_id", "case_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debt_case_one_open",
                schema: "ar",
                table: "debt_case",
                column: "customer_id",
                unique: true,
                filter: "stage IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "debt_case",
                schema: "ar");
        }
    }
}
