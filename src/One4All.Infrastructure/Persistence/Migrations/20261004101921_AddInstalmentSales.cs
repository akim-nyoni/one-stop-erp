using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstalmentSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instalment_agreement",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    agreement_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    buyer_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    deposit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    annual_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    signed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    interest_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    settled_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instalment_agreement", x => x.id);
                    table.CheckConstraint("ck_instalment_agreement_terms", "cash_price > 0 AND deposit >= 0 AND deposit < cash_price AND term_months >= 1 AND annual_rate_percent >= 0");
                    table.ForeignKey(
                        name: "fk_instalment_agreement_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instalment_schedule_line",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instalment_agreement_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    interest = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    principal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    raised = table.Column<bool>(type: "boolean", nullable: false),
                    rebated = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instalment_schedule_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_instalment_schedule_line_instalment_agreement_instalment_ag",
                        column: x => x.instalment_agreement_id,
                        principalSchema: "hse",
                        principalTable: "instalment_agreement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_instalment_agreement_company_id_agreement_number",
                schema: "hse",
                table: "instalment_agreement",
                columns: new[] { "company_id", "agreement_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instalment_agreement_one_active_per_stand",
                schema: "hse",
                table: "instalment_agreement",
                column: "stand_id",
                unique: true,
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_instalment_schedule_line_due_date",
                schema: "hse",
                table: "instalment_schedule_line",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_instalment_schedule_line_instalment_agreement_id_number",
                schema: "hse",
                table: "instalment_schedule_line",
                columns: new[] { "instalment_agreement_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instalment_schedule_line",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "instalment_agreement",
                schema: "hse");
        }
    }
}
