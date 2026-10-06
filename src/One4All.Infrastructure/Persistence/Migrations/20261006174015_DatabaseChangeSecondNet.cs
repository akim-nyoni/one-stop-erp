using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseChangeSecondNet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "db_change",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    occurred_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    table_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    operation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    row_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    old_values = table.Column<string>(type: "jsonb", maxLength: 256, nullable: true),
                    new_values = table.Column<string>(type: "jsonb", maxLength: 256, nullable: true),
                    db_user = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    application_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    client_address = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_db_change", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_db_change_occurred_on_utc",
                schema: "audit",
                table: "db_change",
                column: "occurred_on_utc");

            // The second net (brief §6): triggers on the most sensitive tables record every row change in audit.db_change themselves, so
            // direct SQL is caught as well as the application; secrets are stripped, and the table is append-only.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit.capture_db_change() RETURNS trigger
                LANGUAGE plpgsql SECURITY DEFINER AS $$
                DECLARE
                    old_row jsonb := CASE WHEN TG_OP <> 'INSERT' THEN to_jsonb(OLD) - 'password_hash' - 'pin_hash' - 'two_factor_secret' - 'security_stamp' END;
                    new_row jsonb := CASE WHEN TG_OP <> 'DELETE' THEN to_jsonb(NEW) - 'password_hash' - 'pin_hash' - 'two_factor_secret' - 'security_stamp' END;
                BEGIN
                    INSERT INTO audit.db_change (occurred_on_utc, table_name, operation, row_id, old_values, new_values, db_user, application_name, client_address)
                    VALUES (now(), TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME, TG_OP, COALESCE(new_row ->> 'id', old_row ->> 'id'), old_row, new_row,
                            session_user, NULLIF(current_setting('application_name', true), ''), inet_client_addr()::text);
                    RETURN NULL;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit.db_change_append_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.db_change is append-only: % is not allowed', TG_OP USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_db_change_no_update_delete BEFORE UPDATE OR DELETE ON audit.db_change FOR EACH ROW EXECUTE FUNCTION audit.db_change_append_only();");
            migrationBuilder.Sql("CREATE TRIGGER trg_db_change_no_truncate BEFORE TRUNCATE ON audit.db_change FOR EACH STATEMENT EXECUTE FUNCTION audit.db_change_append_only();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_user AFTER INSERT OR UPDATE OR DELETE ON sec.\"user\" FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_role AFTER INSERT OR UPDATE OR DELETE ON sec.role FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_role_permission AFTER INSERT OR UPDATE OR DELETE ON sec.role_permission FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_sec_user_role AFTER INSERT OR UPDATE OR DELETE ON sec.user_role FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_inv_price_list_line AFTER INSERT OR UPDATE OR DELETE ON inv.price_list_line FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_lic_installed_licence AFTER INSERT OR UPDATE OR DELETE ON lic.installed_licence FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_mun_tariff AFTER INSERT OR UPDATE OR DELETE ON mun.tariff FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_mun_meter AFTER INSERT OR UPDATE OR DELETE ON mun.meter FOR EACH ROW EXECUTE FUNCTION audit.capture_db_change();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_user ON sec.\"user\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_role ON sec.role;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_role_permission ON sec.role_permission;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_sec_user_role ON sec.user_role;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_inv_price_list_line ON inv.price_list_line;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_lic_installed_licence ON lic.installed_licence;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_mun_tariff ON mun.tariff;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_mun_meter ON mun.meter;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_db_change_no_truncate ON audit.db_change;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_db_change_no_update_delete ON audit.db_change;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.db_change_append_only();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.capture_db_change();");


            migrationBuilder.DropTable(
                name: "db_change",
                schema: "audit");
        }
    }
}
