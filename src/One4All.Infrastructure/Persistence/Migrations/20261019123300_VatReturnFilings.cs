using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VatReturnFilings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vat_return",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    output_tax = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    input_tax = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    deferred = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    filed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vat_return", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vat_return_company_id_from_date_to_date",
                schema: "gl",
                table: "vat_return",
                columns: new[] { "company_id", "from_date", "to_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vat_return",
                schema: "gl");
        }
    }
}
