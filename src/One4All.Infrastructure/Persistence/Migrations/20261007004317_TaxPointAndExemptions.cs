using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxPointAndExemptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "tax_point_date",
                schema: "trd",
                table: "open_item",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tax_point_basis",
                schema: "sys",
                table: "company",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "tax_exemption_certificate",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    ledger = table.Column<int>(type: "integer", nullable: false),
                    trader_id = table.Column<long>(type: "bigint", nullable: false),
                    certificate_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    issued_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    revoked_on = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_exemption_certificate", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tax_exemption_certificate_company_id_ledger_trader_id",
                schema: "gl",
                table: "tax_exemption_certificate",
                columns: new[] { "company_id", "ledger", "trader_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_exemption_certificate_company_id_valid_to",
                schema: "gl",
                table: "tax_exemption_certificate",
                columns: new[] { "company_id", "valid_to" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tax_exemption_certificate",
                schema: "gl");

            migrationBuilder.DropColumn(
                name: "tax_point_date",
                schema: "trd",
                table: "open_item");

            migrationBuilder.DropColumn(
                name: "tax_point_basis",
                schema: "sys",
                table: "company");
        }
    }
}
