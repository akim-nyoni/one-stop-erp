using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Ledger guards in the database itself (brief §9), beneath the posting engine's own checks: journal-line sign conventions as CHECK
    /// constraints, and a deferred constraint trigger refusing, at commit, a posted journal that does not balance in base currency.
    /// </summary>
    public partial class AddLedgerDatabaseGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sign conventions (brief §9): amounts are never negative and a line is a debit or a credit, never both.
            migrationBuilder.Sql("""
                ALTER TABLE gl.journal_line
                    ADD CONSTRAINT ck_journal_line_amounts_not_negative
                        CHECK (transaction_debit >= 0 AND transaction_credit >= 0 AND base_debit >= 0 AND base_credit >= 0),
                    ADD CONSTRAINT ck_journal_line_one_side
                        CHECK (NOT (transaction_debit > 0 AND transaction_credit > 0) AND NOT (base_debit > 0 AND base_credit > 0));
                """);

            // A posted journal balances in base currency, checked when the transaction commits (the lines are written after the header).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION gl.assert_journal_balanced(target bigint) RETURNS void
                LANGUAGE plpgsql AS $$
                DECLARE
                    status_now integer;
                    number_now text;
                    difference numeric;
                BEGIN
                    SELECT status, journal_number INTO status_now, number_now FROM gl.journal WHERE id = target;
                    IF status_now IS NULL OR status_now NOT IN (2, 3) THEN
                        RETURN;
                    END IF;

                    SELECT COALESCE(SUM(base_debit), 0) - COALESCE(SUM(base_credit), 0) INTO difference FROM gl.journal_line WHERE journal_entry_id = target;
                    IF difference <> 0 THEN
                        RAISE EXCEPTION 'Posted journal % does not balance in base currency (difference %)', number_now, difference
                            USING ERRCODE = 'check_violation';
                    END IF;
                END;
                $$;

                CREATE OR REPLACE FUNCTION gl.journal_balanced_on_journal() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM gl.assert_journal_balanced(NEW.id);
                    RETURN NULL;
                END;
                $$;

                CREATE OR REPLACE FUNCTION gl.journal_balanced_on_line() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM gl.assert_journal_balanced(NEW.journal_entry_id);
                    RETURN NULL;
                END;
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER trg_journal_line_balanced
                    AFTER INSERT OR UPDATE ON gl.journal_line
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION gl.journal_balanced_on_line();
                CREATE CONSTRAINT TRIGGER trg_journal_balanced
                    AFTER INSERT OR UPDATE OF status ON gl.journal
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION gl.journal_balanced_on_journal();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_journal_balanced ON gl.journal;
                DROP TRIGGER IF EXISTS trg_journal_line_balanced ON gl.journal_line;
                DROP FUNCTION IF EXISTS gl.journal_balanced_on_journal();
                DROP FUNCTION IF EXISTS gl.journal_balanced_on_line();
                DROP FUNCTION IF EXISTS gl.assert_journal_balanced(bigint);
                ALTER TABLE gl.journal_line DROP CONSTRAINT IF EXISTS ck_journal_line_one_side, DROP CONSTRAINT IF EXISTS ck_journal_line_amounts_not_negative;
                """);
        }
    }
}
