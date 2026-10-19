using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashBookVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                schema: "cb",
                table: "bank_rule",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                schema: "vou",
                table: "accounting_voucher_line",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

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

                    -- A VAT line split out of a cash voucher posts to its tax code's own input or output account (a control account).
                    IF NEW.tax_code IS NOT NULL AND EXISTS (SELECT 1 FROM sys.tax_code t WHERE t.company_id = company AND upper(t.code) = upper(NEW.tax_code)
                        AND (t.input_account_code = NEW.account_code OR t.output_account_code = NEW.account_code)) THEN
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

            migrationBuilder.DropColumn(
                name: "tax_code",
                schema: "cb",
                table: "bank_rule");

            migrationBuilder.DropColumn(
                name: "tax_code",
                schema: "vou",
                table: "accounting_voucher_line");
        }
    }
}
