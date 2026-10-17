using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPettyCashFunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "petty_cash_fund",
                schema: "cb",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bank_account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    over_short_account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    float_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    custodian_user_id = table.Column<long>(type: "bigint", nullable: true),
                    branch_id = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_petty_cash_fund", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "petty_cash_count",
                schema: "cb",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    fund_id = table.Column<long>(type: "bigint", nullable: false),
                    count_date = table.Column<DateOnly>(type: "date", nullable: false),
                    counted_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    book_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    counted_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    denominations = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    note = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    variance_journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reimbursement_journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reconciled_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reconciled_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_petty_cash_count", x => x.id);
                    table.ForeignKey(
                        name: "fk_petty_cash_count_petty_cash_fund_fund_id",
                        column: x => x.fund_id,
                        principalSchema: "cb",
                        principalTable: "petty_cash_fund",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_petty_cash_count_company_id_fund_id_count_date",
                schema: "cb",
                table: "petty_cash_count",
                columns: new[] { "company_id", "fund_id", "count_date" });

            migrationBuilder.CreateIndex(
                name: "ix_petty_cash_count_fund_id",
                schema: "cb",
                table: "petty_cash_count",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_petty_cash_fund_company_id_code",
                schema: "cb",
                table: "petty_cash_fund",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "petty_cash_count",
                schema: "cb");

            migrationBuilder.DropTable(
                name: "petty_cash_fund",
                schema: "cb");
        }
    }
}
