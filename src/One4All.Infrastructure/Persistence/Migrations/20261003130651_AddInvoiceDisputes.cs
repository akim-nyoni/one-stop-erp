using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_dispute",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    journal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_dispute", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_dispute_line",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    invoice_dispute_id = table.Column<long>(type: "bigint", nullable: false),
                    purchase_order_line_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_dispute_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_dispute_line_invoice_dispute_invoice_dispute_id",
                        column: x => x.invoice_dispute_id,
                        principalSchema: "pop",
                        principalTable: "invoice_dispute",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_dispute_company_id_status",
                schema: "pop",
                table: "invoice_dispute",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_dispute_line_invoice_dispute_id",
                schema: "pop",
                table: "invoice_dispute_line",
                column: "invoice_dispute_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_dispute_line",
                schema: "pop");

            migrationBuilder.DropTable(
                name: "invoice_dispute",
                schema: "pop");
        }
    }
}
