using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChequesAndStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_mobile_wallet",
                schema: "cb",
                table: "bank_account",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "cheque",
                schema: "cb",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    cheque_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    payee = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    status_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cheque", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_statement",
                schema: "cb",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    statement_date = table.Column<DateOnly>(type: "date", nullable: false),
                    closing_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_statement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_statement_line",
                schema: "cb",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_statement_id = table.Column<long>(type: "bigint", nullable: false),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_statement_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_supplier_statement_line_supplier_statements_supplier_statem",
                        column: x => x.supplier_statement_id,
                        principalSchema: "cb",
                        principalTable: "supplier_statement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cheque_bank_account_id_cheque_number",
                schema: "cb",
                table: "cheque",
                columns: new[] { "bank_account_id", "cheque_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_statement_company_id_supplier_id_statement_date",
                schema: "cb",
                table: "supplier_statement",
                columns: new[] { "company_id", "supplier_id", "statement_date" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_statement_line_supplier_statement_id",
                schema: "cb",
                table: "supplier_statement_line",
                column: "supplier_statement_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cheque",
                schema: "cb");

            migrationBuilder.DropTable(
                name: "supplier_statement_line",
                schema: "cb");

            migrationBuilder.DropTable(
                name: "supplier_statement",
                schema: "cb");

            migrationBuilder.DropColumn(
                name: "is_mobile_wallet",
                schema: "cb",
                table: "bank_account");
        }
    }
}
