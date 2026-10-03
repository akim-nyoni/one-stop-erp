using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes <c>audit.change_log</c> append-only at the database level: any UPDATE, DELETE or TRUNCATE is
    /// refused by trigger, whoever issues it. The application only ever inserts. Someone able to disable the
    /// trigger can still alter rows, which the SHA-256 hash chain then detects (<c>IAuditService.VerifyChainAsync</c>).
    /// </summary>
    public partial class MakeChangeLogAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit.change_log_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.change_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_change_log_no_update_delete
                    BEFORE UPDATE OR DELETE ON audit.change_log
                    FOR EACH ROW EXECUTE FUNCTION audit.change_log_append_only();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_change_log_no_truncate
                    BEFORE TRUNCATE ON audit.change_log
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.change_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_change_log_no_truncate ON audit.change_log;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_change_log_no_update_delete ON audit.change_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.change_log_append_only();");
        }
    }
}
