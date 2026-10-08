using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenDatabaseChangeSecondNet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The second net (brief §6) now also watches the ledger's headers and chart, and every table that decides who may do what: groups,
            // their roles and members, permission overrides and scope rules. Journal lines are watched for any change, and for inserts that do not
            // come from the application (whose own inserts are already in the audit log and balance-checked), so posting volume does not double.
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_gl_journal AFTER INSERT OR UPDATE OR DELETE ON gl.journal FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_gl_account AFTER INSERT OR UPDATE OR DELETE ON gl.account FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_gl_cost_centre AFTER INSERT OR UPDATE OR DELETE ON gl.cost_centre FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_gl_department AFTER INSERT OR UPDATE OR DELETE ON gl.department FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_group AFTER INSERT OR UPDATE OR DELETE ON sec.\"group\" FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_group_role AFTER INSERT OR UPDATE OR DELETE ON sec.group_role FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_user_group AFTER INSERT OR UPDATE OR DELETE ON sec.user_group FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_user_permission_override AFTER INSERT OR UPDATE OR DELETE ON sec.user_permission_override FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_access_scope AFTER INSERT OR UPDATE OR DELETE ON sec.access_scope FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_gl_journal_line_change AFTER UPDATE OR DELETE ON gl.journal_line FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_audit_gl_journal_line_outside_insert AFTER INSERT ON gl.journal_line FOR EACH ROW
                    WHEN (current_setting('application_name', true) IS DISTINCT FROM 'One4All')
                    EXECUTE FUNCTION audit.capture_db_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_gl_journal ON gl.journal;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_gl_account ON gl.account;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_gl_cost_centre ON gl.cost_centre;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_gl_department ON gl.department;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_group ON sec.\"group\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_group_role ON sec.group_role;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_user_group ON sec.user_group;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_user_permission_override ON sec.user_permission_override;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_access_scope ON sec.access_scope;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_gl_journal_line_change ON gl.journal_line;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_gl_journal_line_outside_insert ON gl.journal_line;");
        }
    }
}
