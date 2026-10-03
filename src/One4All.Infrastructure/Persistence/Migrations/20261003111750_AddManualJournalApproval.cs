using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualJournalApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "approved_by_user_id",
                schema: "gl",
                table: "journal",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "awaiting_approval",
                schema: "gl",
                table: "journal",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "prepared_by_user_id",
                schema: "gl",
                table: "journal",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "gl",
                table: "journal",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "manual_journal_approval_threshold",
                schema: "sys",
                table: "company",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_approved_before_posting",
                schema: "gl",
                table: "journal",
                sql: "prepared_by_user_id IS NULL OR status NOT IN (2, 3) OR approved_by_user_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_approver_not_preparer",
                schema: "gl",
                table: "journal",
                sql: "approved_by_user_id IS NULL OR approved_by_user_id <> prepared_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_approved_before_posting",
                schema: "gl",
                table: "journal");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_approver_not_preparer",
                schema: "gl",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "approved_by_user_id",
                schema: "gl",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "awaiting_approval",
                schema: "gl",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "prepared_by_user_id",
                schema: "gl",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "gl",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "manual_journal_approval_threshold",
                schema: "sys",
                table: "company");
        }
    }
}
