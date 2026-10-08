using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartitionAuditLogAndArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archive",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    year = table.Column<int>(type: "integer", nullable: false),
                    table_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    row_count = table.Column<long>(type: "bigint", nullable: false),
                    chain_heads_json = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authorisation_key_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    archived_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_year",
                schema: "audit",
                table: "archive",
                column: "year",
                unique: true);

            // The archive register is append-only like the log it describes.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_archive_append_only BEFORE UPDATE OR DELETE ON audit.archive
                    FOR EACH ROW EXECUTE FUNCTION audit.change_log_append_only();
                """);

            // audit.change_log becomes partitioned by year (brief §2: partitioning by year; §5.1: retention by partition detach). A partitioned
            // table's primary key must hold the partition column, so it becomes (id, occurred_on_utc); ids stay unique through the identity.
            migrationBuilder.Sql("""
                CREATE SCHEMA IF NOT EXISTS audit_archive;

                ALTER TABLE audit.change_log RENAME TO change_log_unpartitioned;
                ALTER TABLE audit.change_log_unpartitioned RENAME CONSTRAINT pk_change_log TO pk_change_log_unpartitioned;
                ALTER INDEX audit.ix_change_log_company_id_occurred_on_utc RENAME TO ix_change_log_unpartitioned_company;
                ALTER INDEX audit.ix_change_log_entity_type_entity_id RENAME TO ix_change_log_unpartitioned_entity;
                ALTER INDEX audit.ix_change_log_user_id RENAME TO ix_change_log_unpartitioned_user;

                CREATE TABLE audit.change_log (LIKE audit.change_log_unpartitioned INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE)
                    PARTITION BY RANGE (occurred_on_utc);
                ALTER TABLE audit.change_log ADD CONSTRAINT pk_change_log PRIMARY KEY (id, occurred_on_utc);
                CREATE INDEX ix_change_log_company_id_occurred_on_utc ON audit.change_log (company_id, occurred_on_utc);
                CREATE INDEX ix_change_log_entity_type_entity_id ON audit.change_log (entity_type, entity_id);
                CREATE INDEX ix_change_log_user_id ON audit.change_log (user_id);

                -- A year's partition, with TRUNCATE refused on it directly (row triggers come from the parent).
                CREATE OR REPLACE FUNCTION audit.ensure_change_log_year(p_year integer) RETURNS void
                LANGUAGE plpgsql AS $$
                DECLARE
                    v_name text := format('change_log_y%s', p_year);
                BEGIN
                    IF to_regclass(format('audit.%I', v_name)) IS NULL THEN
                        EXECUTE format('CREATE TABLE audit.%I PARTITION OF audit.change_log FOR VALUES FROM (%L) TO (%L)', v_name,
                            make_timestamptz(p_year, 1, 1, 0, 0, 0, 'UTC'), make_timestamptz(p_year + 1, 1, 1, 0, 0, 0, 'UTC'));
                        EXECUTE format('CREATE TRIGGER trg_%s_no_truncate BEFORE TRUNCATE ON audit.%I FOR EACH STATEMENT EXECUTE FUNCTION audit.change_log_append_only()',
                            v_name, v_name);
                    END IF;
                END;
                $$;

                DO $$
                DECLARE
                    v_first integer := LEAST(2020, COALESCE((SELECT extract(year FROM min(occurred_on_utc) AT TIME ZONE 'UTC')::integer FROM audit.change_log_unpartitioned), 2020));
                BEGIN
                    FOR y IN v_first .. 2040 LOOP
                        PERFORM audit.ensure_change_log_year(y);
                    END LOOP;
                END;
                $$;
                CREATE TABLE audit.change_log_default PARTITION OF audit.change_log DEFAULT;
                CREATE TRIGGER trg_change_log_default_no_truncate BEFORE TRUNCATE ON audit.change_log_default
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.change_log_append_only();

                INSERT INTO audit.change_log OVERRIDING SYSTEM VALUE SELECT * FROM audit.change_log_unpartitioned;
                SELECT setval(pg_get_serial_sequence('audit.change_log', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM audit.change_log), 1));
                DROP TABLE audit.change_log_unpartitioned;

                CREATE TRIGGER trg_change_log_no_update_delete BEFORE UPDATE OR DELETE ON audit.change_log
                    FOR EACH ROW EXECUTE FUNCTION audit.change_log_append_only();
                CREATE TRIGGER trg_change_log_no_truncate BEFORE TRUNCATE ON audit.change_log
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.change_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to one table: archived years are brought back from audit_archive first.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    v_table record;
                BEGIN
                    FOR v_table IN SELECT tablename FROM pg_tables WHERE schemaname = 'audit_archive' AND tablename LIKE 'change_log_y%' LOOP
                        EXECUTE format('INSERT INTO audit.change_log OVERRIDING SYSTEM VALUE SELECT * FROM audit_archive.%I', v_table.tablename);
                    END LOOP;
                END;
                $$;

                CREATE TABLE audit.change_log_plain (LIKE audit.change_log INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE);
                INSERT INTO audit.change_log_plain OVERRIDING SYSTEM VALUE SELECT * FROM audit.change_log;
                SELECT setval(pg_get_serial_sequence('audit.change_log_plain', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM audit.change_log_plain), 1));
                DROP TABLE audit.change_log CASCADE;
                ALTER TABLE audit.change_log_plain RENAME TO change_log;
                ALTER TABLE audit.change_log ADD CONSTRAINT pk_change_log PRIMARY KEY (id);
                CREATE INDEX ix_change_log_company_id_occurred_on_utc ON audit.change_log (company_id, occurred_on_utc);
                CREATE INDEX ix_change_log_entity_type_entity_id ON audit.change_log (entity_type, entity_id);
                CREATE INDEX ix_change_log_user_id ON audit.change_log (user_id);
                CREATE TRIGGER trg_change_log_no_update_delete BEFORE UPDATE OR DELETE ON audit.change_log
                    FOR EACH ROW EXECUTE FUNCTION audit.change_log_append_only();
                CREATE TRIGGER trg_change_log_no_truncate BEFORE TRUNCATE ON audit.change_log
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.change_log_append_only();
                DROP FUNCTION IF EXISTS audit.ensure_change_log_year(integer);
                DROP SCHEMA IF EXISTS audit_archive CASCADE;
                DROP TRIGGER IF EXISTS trg_archive_append_only ON audit.archive;
                """);

            migrationBuilder.DropTable(
                name: "archive",
                schema: "audit");
        }
    }
}
