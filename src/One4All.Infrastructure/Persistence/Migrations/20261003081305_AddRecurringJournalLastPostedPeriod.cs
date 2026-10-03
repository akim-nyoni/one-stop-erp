using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records the latest month each recurring-journal template was posted for, so re-running a month-end batch
    /// cannot post the same charge twice.
    /// </summary>
    public partial class AddRecurringJournalLastPostedPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "last_posted_period",
                schema: "gl",
                table: "recurring_journal",
                type: "integer",
                nullable: true);

            // Best-effort backfill: the latest month each template was posted under its own code (the default).
            migrationBuilder.Sql("""
                UPDATE gl.recurring_journal r
                SET last_posted_period = j.period
                FROM (
                    SELECT company_id, document_number, max(extract(year FROM posting_date)::int * 100 + extract(month FROM posting_date)::int) AS period
                    FROM gl.journal
                    WHERE document_type = 'Recurring' AND status IN (2, 3)
                    GROUP BY company_id, document_number
                ) j
                WHERE j.company_id = r.company_id AND j.document_number = r.code;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_posted_period",
                schema: "gl",
                table: "recurring_journal");
        }
    }
}
