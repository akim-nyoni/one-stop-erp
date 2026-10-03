using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Brief §3.1: "Control accounts are locked from manual journals. Enforce in code AND with a DB trigger." Also closes a
    /// gap in the posted-journal guard: lines could still be inserted into a posted journal. Journals
    /// now record whether they are manual (<c>is_manual</c>, set by the posting engine), and a row trigger on
    /// <c>gl.journal_line</c> refuses a line on a control account when its journal is manual — whatever wrote it. The
    /// posted-journal guard is redefined to keep <c>is_manual</c> fixed once posted.
    /// </summary>
    public partial class GuardControlAccountsFromManualJournals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_manual",
                schema: "gl",
                table: "journal",
                type: "boolean",
                nullable: false,
                defaultValue: false);

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

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_journal_line_control_guard
                    BEFORE INSERT OR UPDATE OF account_code, journal_entry_id ON gl.journal_line
                    FOR EACH ROW EXECUTE FUNCTION gl.journal_line_control_guard();
                """);

            migrationBuilder.Sql(PostedGuard(includeIsManual: true));

            // Lines can no longer be added to a posted journal either (that would unbalance it) — except while the journal
            // itself is being written, which the posting engine does in the same transaction as its lines. Journals inserted
            // by the current transaction are remembered in a transaction-local setting.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION gl.journal_mark_new() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM set_config('one4all.new_journals',
                        coalesce(current_setting('one4all.new_journals', true), '') || ',' || NEW.id::text || ',', true);
                    RETURN NULL;
                END;
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_journal_mark_new
                    AFTER INSERT ON gl.journal
                    FOR EACH ROW EXECUTE FUNCTION gl.journal_mark_new();
                """);
            migrationBuilder.Sql(LineGuard(guardInserts: true));
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_line_posted_guard ON gl.journal_line;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_journal_line_posted_guard
                    BEFORE INSERT OR UPDATE OR DELETE ON gl.journal_line
                    FOR EACH ROW EXECUTE FUNCTION gl.journal_line_posted_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_line_posted_guard ON gl.journal_line;");
            migrationBuilder.Sql(LineGuard(guardInserts: false));
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_journal_line_posted_guard
                    BEFORE UPDATE OR DELETE ON gl.journal_line
                    FOR EACH ROW EXECUTE FUNCTION gl.journal_line_posted_guard();
                """);
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_mark_new ON gl.journal;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS gl.journal_mark_new();");
            migrationBuilder.Sql(PostedGuard(includeIsManual: false));
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_journal_line_control_guard ON gl.journal_line;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS gl.journal_line_control_guard();");

            migrationBuilder.DropColumn(
                name: "is_manual",
                schema: "gl",
                table: "journal");
        }

        // Refuses a new line on a posted journal unless that journal was inserted by the current transaction (its id is in
        // the transaction-local list kept by gl.journal_mark_new — savepoints included, which xmin would not cover).
        private const string InsertGuard = """
            SELECT status, journal_number INTO parent_status, parent_number FROM gl.journal WHERE id = NEW.journal_entry_id;
                    IF parent_status IN (2, 3)
                        AND position(',' || NEW.journal_entry_id::text || ',' IN coalesce(current_setting('one4all.new_journals', true), '')) = 0 THEN
                        RAISE EXCEPTION 'Lines cannot be added to posted journal %; post a new journal instead', parent_number
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
            """;

        // The posted-journal line guard from MakePostedJournalsImmutable, optionally also refusing new lines on a posted
        // journal that was not written in the current transaction.
        private static string LineGuard(bool guardInserts) => $$"""
            CREATE OR REPLACE FUNCTION gl.journal_line_posted_guard() RETURNS trigger
            LANGUAGE plpgsql AS $$
            DECLARE
                parent_status integer;
                parent_number text;
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    {{(guardInserts ? InsertGuard : string.Empty)}}
                    RETURN NEW;
                END IF;

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
            """;

        // The posted-journal guard from MakePostedJournalsImmutable, optionally also freezing is_manual once posted.
        private static string PostedGuard(bool includeIsManual) => $$"""
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
                    {{(includeIsManual ? "OR NEW.is_manual IS DISTINCT FROM OLD.is_manual" : string.Empty)}}
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
            """;
    }
}
