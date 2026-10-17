using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddThirteenthAuditColumnsAndManualJournalGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "assumption_approved_by_user_id",
                schema: "hse",
                table: "transfer_application",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assumption_clearing_account_code",
                schema: "hse",
                table: "transfer_application",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "balance_assumed",
                schema: "hse",
                table: "transfer_application",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "balance_assumed_amount",
                schema: "hse",
                table: "transfer_application",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "invoice_price_tolerance_percent",
                schema: "ap",
                table: "supplier",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "invoice_quantity_tolerance_percent",
                schema: "ap",
                table: "supplier",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deferred_posting_modules",
                schema: "sys",
                table: "company",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "invoice_price_tolerance_percent",
                schema: "sys",
                table: "company",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "invoice_quantity_tolerance_percent",
                schema: "sys",
                table: "company",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "activated_by_user_id",
                schema: "bud",
                table: "budget",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "activated_on",
                schema: "bud",
                table: "budget",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_monetary",
                schema: "gl",
                table: "account",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The database guard also refuses a manual journal line on an account that does not allow manual journals (thirteenth audit #2).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION gl.journal_line_control_guard() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    manual boolean;
                    company bigint;
                    number text;
                    control integer;
                    allowed boolean;
                BEGIN
                    SELECT is_manual, company_id, journal_number INTO manual, company, number FROM gl.journal WHERE id = NEW.journal_entry_id;
                    IF manual IS DISTINCT FROM TRUE THEN
                        RETURN NEW;
                    END IF;

                    SELECT control_account_type, allow_manual_journal INTO control, allowed FROM gl.account WHERE company_id = company AND account_code = NEW.account_code;
                    IF control IS NOT NULL AND control <> 0 THEN
                        RAISE EXCEPTION 'Account % is a control account and cannot be posted by manual journal (journal %)', NEW.account_code, number
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF allowed IS DISTINCT FROM TRUE THEN
                        RAISE EXCEPTION 'Account % does not allow manual journals (journal %)', NEW.account_code, number
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION gl.journal_line_control_guard() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    manual boolean;
                    company bigint;
                    number text;
                    control integer;
                BEGIN
                    SELECT is_manual, company_id, journal_number INTO manual, company, number FROM gl.journal WHERE id = NEW.journal_entry_id;
                    IF manual IS DISTINCT FROM TRUE THEN
                        RETURN NEW;
                    END IF;

                    SELECT control_account_type INTO control FROM gl.account WHERE company_id = company AND account_code = NEW.account_code;
                    IF control IS NOT NULL AND control <> 0 THEN
                        RAISE EXCEPTION 'Account % is a control account and cannot be posted by manual journal (journal %)', NEW.account_code, number
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);
            migrationBuilder.DropColumn(
                name: "assumption_approved_by_user_id",
                schema: "hse",
                table: "transfer_application");

            migrationBuilder.DropColumn(
                name: "assumption_clearing_account_code",
                schema: "hse",
                table: "transfer_application");

            migrationBuilder.DropColumn(
                name: "balance_assumed",
                schema: "hse",
                table: "transfer_application");

            migrationBuilder.DropColumn(
                name: "balance_assumed_amount",
                schema: "hse",
                table: "transfer_application");

            migrationBuilder.DropColumn(
                name: "invoice_price_tolerance_percent",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "invoice_quantity_tolerance_percent",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "deferred_posting_modules",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "invoice_price_tolerance_percent",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "invoice_quantity_tolerance_percent",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "activated_by_user_id",
                schema: "bud",
                table: "budget");

            migrationBuilder.DropColumn(
                name: "activated_on",
                schema: "bud",
                table: "budget");

            migrationBuilder.DropColumn(
                name: "is_monetary",
                schema: "gl",
                table: "account");
        }
    }
}
