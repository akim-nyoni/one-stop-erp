using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingLedgers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trd");

            migrationBuilder.EnsureSchema(
                name: "ar");

            migrationBuilder.EnsureSchema(
                name: "ap");

            migrationBuilder.CreateTable(
                name: "allocation",
                schema: "trd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    ledger = table.Column<int>(type: "integer", nullable: false),
                    trader_id = table.Column<long>(type: "bigint", nullable: false),
                    allocation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    debit_item_id = table.Column<long>(type: "bigint", nullable: false),
                    credit_item_id = table.Column<long>(type: "bigint", nullable: false),
                    transaction_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    debit_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    credit_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    realised_fx_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    payment_terms_days = table.Column<int>(type: "integer", nullable: false),
                    settlement_discount_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ageing_basis = table.Column<int>(type: "integer", nullable: false),
                    statement_type = table.Column<int>(type: "integer", nullable: false),
                    on_hold = table.Column<bool>(type: "boolean", nullable: false),
                    tax_registration = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    default_tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sales_agent = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
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
                    table.PrimaryKey("pk_customer", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "open_item",
                schema: "trd",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    ledger = table.Column<int>(type: "integer", nullable: false),
                    trader_id = table.Column<long>(type: "bigint", nullable: false),
                    item_type = table.Column<int>(type: "integer", nullable: false),
                    sign = table.Column<int>(type: "integer", nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    original_transaction = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    original_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    outstanding_transaction = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    outstanding_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    narrative = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "supplier",
                schema: "ap",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_terms_days = table.Column<int>(type: "integer", nullable: false),
                    ageing_basis = table.Column<int>(type: "integer", nullable: false),
                    on_hold = table.Column<bool>(type: "boolean", nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    tax_registration = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tax_clearance_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bank_account_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
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
                    table.PrimaryKey("pk_supplier", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_company_id_ledger_trader_id",
                schema: "trd",
                table: "allocation",
                columns: new[] { "company_id", "ledger", "trader_id" });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_credit_item_id",
                schema: "trd",
                table: "allocation",
                column: "credit_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_debit_item_id",
                schema: "trd",
                table: "allocation",
                column: "debit_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_company_id_code",
                schema: "ar",
                table: "customer",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_open_item_company_id_ledger_trader_id",
                schema: "trd",
                table: "open_item",
                columns: new[] { "company_id", "ledger", "trader_id" });

            migrationBuilder.CreateIndex(
                name: "ix_open_item_company_id_ledger_trader_id_outstanding_transacti",
                schema: "trd",
                table: "open_item",
                columns: new[] { "company_id", "ledger", "trader_id", "outstanding_transaction" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_company_id_code",
                schema: "ap",
                table: "supplier",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocation",
                schema: "trd");

            migrationBuilder.DropTable(
                name: "customer",
                schema: "ar");

            migrationBuilder.DropTable(
                name: "open_item",
                schema: "trd");

            migrationBuilder.DropTable(
                name: "supplier",
                schema: "ap");
        }
    }
}
