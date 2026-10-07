using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetClassesAndImpairment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "accumulated_impairment",
                schema: "fa",
                table: "asset",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "asset_class",
                schema: "fa",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    useful_life_months = table.Column<int>(type: "integer", nullable: false),
                    residual_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    reducing_balance_rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    cost_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accumulated_depreciation_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    depreciation_expense_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    impairment_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_class", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "impairment",
                schema: "fa",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    fixed_asset_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    carrying_amount_before = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    recoverable_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_impairment", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_class_company_id_code",
                schema: "fa",
                table: "asset_class",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_impairment_fixed_asset_id",
                schema: "fa",
                table: "impairment",
                column: "fixed_asset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_class",
                schema: "fa");

            migrationBuilder.DropTable(
                name: "impairment",
                schema: "fa");

            migrationBuilder.DropColumn(
                name: "accumulated_impairment",
                schema: "fa",
                table: "asset");
        }
    }
}
