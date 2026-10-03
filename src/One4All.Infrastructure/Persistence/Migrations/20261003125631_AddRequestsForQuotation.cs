using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestsForQuotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rfq",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    rfq_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closes_on = table.Column<DateOnly>(type: "date", nullable: false),
                    invited_supplier_ids = table.Column<List<long>>(type: "bigint[]", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    awarded_supplier_id = table.Column<long>(type: "bigint", nullable: true),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: true),
                    awarded_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    award_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rfq_line",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rfq_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_line_rfq_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "pop",
                        principalTable: "rfq",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_quote",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rfq_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    terms = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_quote", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_quote_rfq_rfq_id",
                        column: x => x.rfq_id,
                        principalSchema: "pop",
                        principalTable: "rfq",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_quote_line",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rfq_quote_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rfq_quote_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_rfq_quote_line_rfq_quote_rfq_quote_id",
                        column: x => x.rfq_quote_id,
                        principalSchema: "pop",
                        principalTable: "rfq_quote",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rfq_company_id_rfq_number",
                schema: "pop",
                table: "rfq",
                columns: new[] { "company_id", "rfq_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfq_line_rfq_id",
                schema: "pop",
                table: "rfq_line",
                column: "rfq_id");

            migrationBuilder.CreateIndex(
                name: "ix_rfq_quote_rfq_id_supplier_id",
                schema: "pop",
                table: "rfq_quote",
                columns: new[] { "rfq_id", "supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rfq_quote_line_rfq_quote_id",
                schema: "pop",
                table: "rfq_quote_line",
                column: "rfq_quote_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rfq_line",
                schema: "pop");

            migrationBuilder.DropTable(
                name: "rfq_quote_line",
                schema: "pop");

            migrationBuilder.DropTable(
                name: "rfq_quote",
                schema: "pop");

            migrationBuilder.DropTable(
                name: "rfq",
                schema: "pop");
        }
    }
}
