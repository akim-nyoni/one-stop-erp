using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FundProjectBranchAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branch",
                schema: "gl",
                table: "journal_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fund",
                schema: "gl",
                table: "journal_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project",
                schema: "gl",
                table: "journal_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_fund",
                schema: "sys",
                table: "company",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "fund_accounting",
                schema: "sys",
                table: "company",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "branch",
                schema: "bud",
                table: "budget_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fund",
                schema: "bud",
                table: "budget_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project",
                schema: "bud",
                table: "budget_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_fund",
                schema: "gl",
                table: "account",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "requires_project",
                schema: "gl",
                table: "account",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "fund",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_restricted = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("pk_fund", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    fund_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("pk_project", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_line_fund",
                schema: "gl",
                table: "journal_line",
                column: "fund",
                filter: "fund IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_journal_line_project",
                schema: "gl",
                table: "journal_line",
                column: "project",
                filter: "project IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_fund_company_id_code",
                schema: "gl",
                table: "fund",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_project_company_id_code",
                schema: "gl",
                table: "project",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fund",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "project",
                schema: "gl");

            migrationBuilder.DropIndex(
                name: "ix_journal_line_fund",
                schema: "gl",
                table: "journal_line");

            migrationBuilder.DropIndex(
                name: "ix_journal_line_project",
                schema: "gl",
                table: "journal_line");

            migrationBuilder.DropColumn(
                name: "branch",
                schema: "gl",
                table: "journal_line");

            migrationBuilder.DropColumn(
                name: "fund",
                schema: "gl",
                table: "journal_line");

            migrationBuilder.DropColumn(
                name: "project",
                schema: "gl",
                table: "journal_line");

            migrationBuilder.DropColumn(
                name: "default_fund",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "fund_accounting",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "branch",
                schema: "bud",
                table: "budget_line");

            migrationBuilder.DropColumn(
                name: "fund",
                schema: "bud",
                table: "budget_line");

            migrationBuilder.DropColumn(
                name: "project",
                schema: "bud",
                table: "budget_line");

            migrationBuilder.DropColumn(
                name: "requires_fund",
                schema: "gl",
                table: "account");

            migrationBuilder.DropColumn(
                name: "requires_project",
                schema: "gl",
                table: "account");
        }
    }
}
