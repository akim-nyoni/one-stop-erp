using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillBillingRunPosted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Runs posted before chunked posting posted every included bill at once: mark those bills posted.
            migrationBuilder.Sql("UPDATE mun.billing_run_line l SET is_posted = TRUE FROM mun.billing_run r WHERE l.billing_run_id = r.id AND r.status = 1 AND l.included;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
