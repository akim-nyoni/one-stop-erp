using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives yearly-reset number sequences a counter per year (<c>sys.number_sequence_year</c>). Previously a single
    /// counter restarted at 1 whenever the document year changed, so alternating postings between two open years
    /// re-issued journal numbers and failed on the unique journal-number index.
    /// </summary>
    public partial class AddNumberSequenceYearCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "number_sequence_year",
                schema: "sys",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    number_sequence_id = table.Column<long>(type: "bigint", nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequence_year", x => new { x.number_sequence_id, x.year });
                    table.ForeignKey(
                        name: "fk_number_sequence_year_number_sequence_number_sequence_id",
                        column: x => x.number_sequence_id,
                        principalSchema: "sys",
                        principalTable: "number_sequence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Carry each yearly sequence's live counter into its year row.
            migrationBuilder.Sql("""
                INSERT INTO sys.number_sequence_year (number_sequence_id, year, next_value)
                SELECT id, current_year, next_value FROM sys.number_sequence
                WHERE reset_yearly AND current_year > 0;
                """);

            // Earlier years of the journal sequence lost their counters at each reset; rebuild them from the
            // highest number already issued per year (JNL-yyyy-nnnnnn) so a back-dated posting cannot reuse one.
            migrationBuilder.Sql("""
                INSERT INTO sys.number_sequence_year (number_sequence_id, year, next_value)
                SELECT s.id, j.yr, j.max_no + 1
                FROM sys.number_sequence s
                JOIN (
                    SELECT company_id,
                           substring(journal_number FROM 5 FOR 4)::int AS yr,
                           max(substring(journal_number FROM 10)::bigint) AS max_no
                    FROM gl.journal
                    WHERE journal_number ~ '^JNL-[0-9]{4}-[0-9]+$'
                    GROUP BY company_id, substring(journal_number FROM 5 FOR 4)
                ) j ON j.company_id = s.company_id
                WHERE s.document_type = 'GLJournal' AND s.branch_id IS NULL AND s.reset_yearly
                ON CONFLICT (number_sequence_id, year)
                DO UPDATE SET next_value = GREATEST(sys.number_sequence_year.next_value, EXCLUDED.next_value);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_sequence_year",
                schema: "sys");
        }
    }
}
