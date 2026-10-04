using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHousingGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "housing_grant",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    grant_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    grantor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    budgeted_expenditure = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    conditions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    deferred_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    income_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conditions_assured_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expenditure_to_date = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    recognised_to_date = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_housing_grant", x => x.id);
                    table.CheckConstraint("ck_housing_grant_amounts", "amount > 0 AND budgeted_expenditure > 0 AND recognised_to_date >= 0 AND recognised_to_date <= amount");
                });

            migrationBuilder.CreateIndex(
                name: "ix_housing_grant_company_id_grant_number",
                schema: "hse",
                table: "housing_grant",
                columns: new[] { "company_id", "grant_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "housing_grant",
                schema: "hse");
        }
    }
}
