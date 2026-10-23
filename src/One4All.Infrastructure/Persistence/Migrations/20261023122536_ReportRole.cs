using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The read-only reporting role user-authored report queries run as (SET LOCAL ROLE one4all_report): SELECT on the reportable business
    /// schemas only, none on security, payroll, audit, licensing, system, sync, messaging or the catalogues. Idempotent: the role is created
    /// once per cluster, the grants are re-applied on every run, and the application user is made a member so it can take the role.
    /// </summary>
    public partial class ReportRole : Migration
    {
        // Kept in step with SqlReportDataProvider.ReportableSchemas.
        private static readonly string[] Schemas = ["gl", "ar", "ap", "trd", "cb", "inv", "pos", "res", "vou", "pop", "sop", "mun", "hse", "sch", "job", "fa", "hr", "exp", "bud", "crm", "rcp", "rpt", "wf"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Without the right to create or grant roles the migration still completes; report queries then refuse to run until an administrator creates the role.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'one4all_report') THEN
                        CREATE ROLE one4all_report NOLOGIN NOINHERIT;
                    END IF;
                    EXECUTE 'GRANT one4all_report TO ' || quote_ident(current_user);
                EXCEPTION WHEN insufficient_privilege THEN
                    RAISE NOTICE 'one4all_report could not be created or granted: %', SQLERRM;
                END
                $$;
                """);
            foreach (string schema in Schemas)
            {
                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = '{schema}') THEN
                            EXECUTE 'GRANT USAGE ON SCHEMA {schema} TO one4all_report';
                            EXECUTE 'GRANT SELECT ON ALL TABLES IN SCHEMA {schema} TO one4all_report';
                            EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT SELECT ON TABLES TO one4all_report';
                        END IF;
                    EXCEPTION WHEN insufficient_privilege OR undefined_object THEN
                        RAISE NOTICE 'one4all_report could not be granted {schema}: %', SQLERRM;
                    END
                    $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (string schema in Schemas)
            {
                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = '{schema}') THEN
                            EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} REVOKE SELECT ON TABLES FROM one4all_report';
                            EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA {schema} FROM one4all_report';
                            EXECUTE 'REVOKE USAGE ON SCHEMA {schema} FROM one4all_report';
                        END IF;
                    END
                    $$;
                    """);
            }

            migrationBuilder.Sql("REVOKE one4all_report FROM CURRENT_USER;");
            migrationBuilder.Sql("DROP ROLE IF EXISTS one4all_report;");
        }
    }
}
