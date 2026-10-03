using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWithholdingCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "withholding_certificate",
                schema: "trd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    certificate_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    payment_open_item_id = table.Column<long>(type: "bigint", nullable: false),
                    payment_document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    withheld_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    remitted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    remittance_journal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_withholding_certificate", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_withholding_certificate_company_id_payment_date",
                schema: "trd",
                table: "withholding_certificate",
                columns: new[] { "company_id", "payment_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "withholding_certificate",
                schema: "trd");
        }
    }
}
