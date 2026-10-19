using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuardOwnershipChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The chain of ownership (brief §4.10, §6): each link is hashed to the one before, and the database itself now refuses to change or remove a
            // link, whoever asks - the application only ever adds one. Every link added is recorded in the database change log, and every change to
            // co-ownership and sectional-title ownership too, so an edit made outside the application shows up there.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION hse.ownership_chain_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'hse.ownership_record is append-only: % is not allowed (record a new ownership change instead)', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_ownership_record_no_update_delete BEFORE UPDATE OR DELETE ON hse.ownership_record FOR EACH ROW EXECUTE FUNCTION hse.ownership_chain_append_only();");
            migrationBuilder.Sql("CREATE TRIGGER trg_ownership_record_no_truncate BEFORE TRUNCATE ON hse.ownership_record FOR EACH STATEMENT EXECUTE FUNCTION hse.ownership_chain_append_only();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_hse_ownership_record AFTER INSERT ON hse.ownership_record FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            foreach (string table in Watched)
            {
                migrationBuilder.Sql($"CREATE TRIGGER trg_audit_{table.Replace('.', '_')} AFTER INSERT OR UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            }
        }

        // Shares in a stand and in a sectional-title unit: every change.
        private static readonly string[] Watched = ["hse.stand_co_ownership", "hse.unit_ownership"];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (string table in Watched)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_audit_{table.Replace('.', '_')} ON {table};");
            }

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_hse_ownership_record ON hse.ownership_record;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ownership_record_no_truncate ON hse.ownership_record;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ownership_record_no_update_delete ON hse.ownership_record;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS hse.ownership_chain_append_only();");
        }
    }
}
