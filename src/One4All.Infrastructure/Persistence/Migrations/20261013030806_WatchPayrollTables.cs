using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WatchPayrollTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The second net over payroll (brief §6, §4.15 "database triggers on payroll tables"): who is paid what, through which elements, tax tables,
            // bank splits, loans and garnishees, every change recorded by the database itself; payslips and their lines when changed, or inserted
            // from outside the application (whose own payroll runs are in the audit log already).
            foreach (string table in Watched)
            {
                migrationBuilder.Sql($"CREATE TRIGGER trg_audit_{table.Replace('.', '_')} AFTER INSERT OR UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            }

            foreach (string table in Results)
            {
                string name = table.Replace('.', '_');
                migrationBuilder.Sql($"CREATE TRIGGER trg_audit_{name}_change AFTER UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
                migrationBuilder.Sql($"""
                    CREATE TRIGGER trg_audit_{name}_outside_insert AFTER INSERT ON {table} FOR EACH ROW
                        WHEN (current_setting('application_name', true) IS DISTINCT FROM 'One4All')
                        EXECUTE FUNCTION audit.capture_db_change();
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (string table in Watched)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_audit_{table.Replace('.', '_')} ON {table};");
            }

            foreach (string table in Results)
            {
                string name = table.Replace('.', '_');
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_audit_{name}_change ON {table};");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_audit_{name}_outside_insert ON {table};");
            }
        }

        // Payroll master data: every change.
        private static readonly string[] Watched =
        [
            "hr.employee", "hr.employee_pay_element", "pay.element", "hr.tax_table", "hr.tax_band", "hr.payment_split", "hr.salary_scale", "pay.employee_loan",
            "hr.garnishee_order", "pay.payroll_variable", "pay.employee_benefit", "hr.employee_tax_credit",
        ];

        // Payroll results: changes, and inserts from outside the application.
        private static readonly string[] Results = ["pay.payslip", "pay.payslip_line"];
    }
}
