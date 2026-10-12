using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCapitalCarryForwardAndRentalPropertyAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "asset_account_code",
                schema: "hse",
                table: "rental_unit",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "carrying_amount",
                schema: "hse",
                table: "rental_unit",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_valued_on",
                schema: "hse",
                table: "rental_unit",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "treatment",
                schema: "hse",
                table: "rental_unit",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "carry_forward_id",
                schema: "bud",
                table: "budget_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "capital_carry_forward",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    from_fiscal_year_id = table.Column<long>(type: "bigint", nullable: false),
                    to_budget_id = table.Column<long>(type: "bigint", nullable: false),
                    account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capital_carry_forward", x => x.id);
                    table.CheckConstraint("ck_capital_carry_forward_amount", "amount > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_capital_carry_forward_company_id_from_fiscal_year_id_accoun",
                schema: "bud",
                table: "capital_carry_forward",
                columns: new[] { "company_id", "from_fiscal_year_id", "account_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capital_carry_forward",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "asset_account_code",
                schema: "hse",
                table: "rental_unit");

            migrationBuilder.DropColumn(
                name: "carrying_amount",
                schema: "hse",
                table: "rental_unit");

            migrationBuilder.DropColumn(
                name: "last_valued_on",
                schema: "hse",
                table: "rental_unit");

            migrationBuilder.DropColumn(
                name: "treatment",
                schema: "hse",
                table: "rental_unit");

            migrationBuilder.DropColumn(
                name: "carry_forward_id",
                schema: "bud",
                table: "budget_line");
        }
    }
}
