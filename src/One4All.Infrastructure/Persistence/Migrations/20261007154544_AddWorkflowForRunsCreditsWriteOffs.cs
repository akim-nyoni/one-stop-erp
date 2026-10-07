using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowForRunsCreditsWriteOffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "write_off_account_code",
                schema: "ar",
                table: "debt_case",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pending_credit_note",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    sales_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    narrative = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    submitted_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_credit_note", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_credit_note_company_id_document_number",
                schema: "ar",
                table: "pending_credit_note",
                columns: new[] { "company_id", "document_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_credit_note",
                schema: "ar");

            migrationBuilder.DropColumn(
                name: "write_off_account_code",
                schema: "ar",
                table: "debt_case");
        }
    }
}
