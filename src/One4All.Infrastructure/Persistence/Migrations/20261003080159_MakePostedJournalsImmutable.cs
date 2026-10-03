using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes posted journals immutable at the database level (brief §3.2: "there is no other way to write to the
    /// ledger"). Once a journal is Posted (2) or Reversed (3):
    /// <list type="bullet">
    /// <item>its lines cannot be deleted, and no line's journal, account, dimensions, currency, rate or amounts can change;</item>
    /// <item>the journal cannot be deleted, and its number, company, dates, period, module, currencies, document fields and
    /// narrative cannot change — the only allowed changes are Posted → Reversed, setting the reversal link once, and the
    /// audit stamps.</item>
    /// </list>
    /// Corrections are made by reversal, as the posting engine already does. Draft and deferred journals are unaffected.
    /// </summary>
    public partial class MakePostedJournalsImmutable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION gl.journal_posted_guard() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.status NOT IN (2, 3) THEN
                        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Journal % is posted and cannot be deleted; reverse it instead', OLD.journal_number
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF NEW.company_id IS DISTINCT FROM OLD.company_id
                        OR NEW.journal_number IS DISTINCT FROM OLD.journal_number
                        OR NEW.batch_number IS DISTINCT FROM OLD.batch_number
                        OR NEW.source_module IS DISTINCT FROM OLD.source_module
                        OR NEW.document_type IS DISTINCT FROM OLD.document_type
                        OR NEW.document_number IS DISTINCT FROM OLD.document_number
                        OR NEW.reference IS DISTINCT FROM OLD.reference
                        OR NEW.narrative IS DISTINCT FROM OLD.narrative
                        OR NEW.posting_date IS DISTINCT FROM OLD.posting_date
                        OR NEW.base_currency IS DISTINCT FROM OLD.base_currency
                        OR NEW.transaction_currency IS DISTINCT FROM OLD.transaction_currency
                        OR NEW.fiscal_year_id IS DISTINCT FROM OLD.fiscal_year_id
                        OR NEW.fiscal_period_id IS DISTINCT FROM OLD.fiscal_period_id
                        OR NEW.created_on_utc IS DISTINCT FROM OLD.created_on_utc
                        OR NOT (NEW.status = OLD.status OR (OLD.status = 2 AND NEW.status = 3))
                        OR (OLD.reversal_of_journal_id IS NOT NULL AND NEW.reversal_of_journal_id IS DISTINCT FROM OLD.reversal_of_journal_id)
                        OR (OLD.is_reversal AND NOT NEW.is_reversal)
                    THEN
                        RAISE EXCEPTION 'Journal % is posted and cannot be altered; reverse it instead', OLD.journal_number
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_journal_posted_guard
                    BEFORE UPDATE OR DELETE ON gl.journal
                    FOR EACH ROW EXECUTE FUNCTION gl.journal_posted_guard();
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION gl.journal_line_posted_guard() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    parent_status integer;
                    parent_number text;
                BEGIN
                    SELECT status, journal_number INTO parent_status, parent_number FROM gl.journal WHERE id = OLD.journal_entry_id;
                    IF parent_status IS NULL OR parent_status NOT IN (2, 3) THEN
                        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Lines of posted journal % cannot be deleted; reverse it instead', parent_number
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF NEW.journal_entry_id IS DISTINCT FROM OLD.journal_entry_id
                        OR NEW.account_code IS DISTINCT FROM OLD.account_code
                        OR NEW.cost_centre IS DISTINCT FROM OLD.cost_centre
                        OR NEW.department IS DISTINCT FROM OLD.department
                        OR NEW.transaction_currency IS DISTINCT FROM OLD.transaction_currency
                        OR NEW.exchange_rate IS DISTINCT FROM OLD.exchange_rate
                        OR NEW.transaction_debit IS DISTINCT FROM OLD.transaction_debit
                        OR NEW.transaction_credit IS DISTINCT FROM OLD.transaction_credit
                        OR NEW.base_debit IS DISTINCT FROM OLD.base_debit
                        OR NEW.base_credit IS DISTINCT FROM OLD.base_credit
                        OR NEW.reporting_debit IS DISTINCT FROM OLD.reporting_debit
                        OR NEW.reporting_credit IS DISTINCT FROM OLD.reporting_credit
                        OR NEW.tax_code IS DISTINCT FROM OLD.tax_code
                    THEN
                        RAISE EXCEPTION 'Lines of posted journal % cannot be altered; reverse it instead', parent_number
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_journal_line_posted_guard
                    BEFORE UPDATE OR DELETE ON gl.journal_line
                    FOR EACH ROW EXECUTE FUNCTION gl.journal_line_posted_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_line_posted_guard ON gl.journal_line;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS gl.journal_line_posted_guard();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_posted_guard ON gl.journal;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS gl.journal_posted_guard();");
        }
    }
}
