using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartitionLineTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The journal, sale and payslip line tables become partitioned by year (brief §2), each line stamped with its header's company and date
            // (the partition key must be in the primary key, which becomes (id, date); ids stay unique through the identity). A year's partition is
            // made by {schema}.ensure_{table}_year for the years in use and two ahead, and a DEFAULT partition catches anything else. The tables' triggers
            // come back on the partitioned table, which clones them onto every partition. Done in SQL: EF Core does not model partitions.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS sys.line_partition_trigger (relation text NOT NULL, trigger_name text NOT NULL, is_constraint boolean NOT NULL, definition text NOT NULL,
                    PRIMARY KEY (relation, trigger_name));
                CREATE TABLE IF NOT EXISTS sys.line_partition_dependent (relation text NOT NULL, name text NOT NULL, kind text NOT NULL, definition text NOT NULL,
                    PRIMARY KEY (relation, name));

                -- gl.journal_line: the lines carry their header's company and date, then the table is rebuilt partitioned by year of posting_date.
                ALTER TABLE gl.journal_line ADD COLUMN IF NOT EXISTS company_id bigint, ADD COLUMN IF NOT EXISTS posting_date date;
                -- The backfill is not a business change: the table's own triggers (posted-row guard, balance check, audit capture) stay quiet for it.
                ALTER TABLE gl.journal_line DISABLE TRIGGER USER;
                UPDATE gl.journal_line l SET company_id = h.company_id, posting_date = h.posting_date FROM gl.journal h WHERE h.id = l.journal_entry_id AND (l.company_id IS NULL OR l.posting_date IS NULL);
                ALTER TABLE gl.journal_line ENABLE TRIGGER USER;
                ALTER TABLE gl.journal_line ALTER COLUMN company_id SET NOT NULL, ALTER COLUMN posting_date SET NOT NULL;

                -- The table's own triggers, kept to put back on the partitioned table (constraint triggers go on each partition).
                INSERT INTO sys.line_partition_trigger (relation, trigger_name, is_constraint, definition)
                SELECT 'gl.journal_line', tgname, tgconstraint <> 0, pg_get_triggerdef(oid) FROM pg_trigger WHERE tgrelid = 'gl.journal_line'::regclass AND NOT tgisinternal
                ON CONFLICT (relation, trigger_name) DO UPDATE SET definition = EXCLUDED.definition, is_constraint = EXCLUDED.is_constraint;

                -- Views and materialised views on the table (a reporting snapshot), kept with their indexes and put back after the rebuild.
                INSERT INTO sys.line_partition_dependent (relation, name, kind, definition)
                SELECT DISTINCT 'gl.journal_line', n.nspname || '.' || c.relname, c.relkind::text, pg_get_viewdef(c.oid, true)
                FROM pg_depend d JOIN pg_rewrite r ON r.oid = d.objid JOIN pg_class c ON c.oid = r.ev_class JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE d.refobjid = 'gl.journal_line'::regclass AND c.oid <> 'gl.journal_line'::regclass AND c.relkind IN ('v', 'm')
                ON CONFLICT (relation, name) DO UPDATE SET definition = EXCLUDED.definition;
                INSERT INTO sys.line_partition_dependent (relation, name, kind, definition)
                SELECT 'gl.journal_line', i.schemaname || '.' || i.indexname, 'i', i.indexdef FROM pg_indexes i
                WHERE i.schemaname || '.' || i.tablename IN (SELECT name FROM sys.line_partition_dependent WHERE relation = 'gl.journal_line' AND kind = 'm')
                ON CONFLICT (relation, name) DO UPDATE SET definition = EXCLUDED.definition;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind FROM sys.line_partition_dependent WHERE relation = 'gl.journal_line' AND kind IN ('v', 'm') LOOP
                        EXECUTE format('DROP %s IF EXISTS %s CASCADE', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name);
                    END LOOP;
                END;
                $$;

                ALTER TABLE gl.journal_line RENAME TO journal_line_unpartitioned;
                ALTER TABLE gl.journal_line_unpartitioned RENAME CONSTRAINT pk_journal_line TO pk_journal_line_unpartitioned;
                ALTER INDEX gl.ix_journal_line_account_code_cost_centre_department RENAME TO ix_journal_line_account_code_cost_centre_department_unpartitioned;
                ALTER INDEX gl.ix_journal_line_fund RENAME TO ix_journal_line_fund_unpartitioned;
                ALTER INDEX gl.ix_journal_line_journal_entry_id RENAME TO ix_journal_line_journal_entry_id_unpartitioned;
                ALTER INDEX gl.ix_journal_line_project RENAME TO ix_journal_line_project_unpartitioned;

                CREATE TABLE gl.journal_line (LIKE gl.journal_line_unpartitioned INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE INCLUDING CONSTRAINTS)
                    PARTITION BY RANGE (posting_date);
                ALTER TABLE gl.journal_line ADD CONSTRAINT pk_journal_line PRIMARY KEY (id, posting_date);
                ALTER TABLE gl.journal_line ADD CONSTRAINT fk_journal_line_journals_journal_entry_id FOREIGN KEY (journal_entry_id) REFERENCES gl.journal (id) ON DELETE CASCADE;
                CREATE INDEX ix_journal_line_account_code_cost_centre_department ON gl.journal_line (account_code, cost_centre, department);
                CREATE INDEX ix_journal_line_fund ON gl.journal_line (fund) WHERE fund IS NOT NULL;
                CREATE INDEX ix_journal_line_journal_entry_id ON gl.journal_line (journal_entry_id);
                CREATE INDEX ix_journal_line_project ON gl.journal_line (project) WHERE project IS NOT NULL;
                CREATE INDEX ix_journal_line_company_id_posting_date ON gl.journal_line (company_id, posting_date);

                -- A year's partition (the partitioned table's triggers, constraint triggers included, are cloned onto it by PostgreSQL).
                CREATE OR REPLACE FUNCTION gl.ensure_journal_line_year(p_year integer) RETURNS void
                LANGUAGE plpgsql AS $$
                DECLARE
                    v_name text := format('journal_line_y%s', p_year);
                BEGIN
                    IF to_regclass(format('gl.%I', v_name)) IS NULL THEN
                        EXECUTE format('CREATE TABLE gl.%I PARTITION OF gl.journal_line FOR VALUES FROM (%L) TO (%L)', v_name, make_date(p_year, 1, 1), make_date(p_year + 1, 1, 1));
                    END IF;
                END;
                $$;

                DO $$
                DECLARE
                    v_now integer := extract(year FROM current_date)::integer;
                    v_first integer := LEAST(v_now - 1, COALESCE((SELECT extract(year FROM min(posting_date))::integer FROM gl.journal_line_unpartitioned), v_now));
                    v_trigger record;
                BEGIN
                    -- Only the years in use and two ahead: a lookup by header id probes every partition, so there are as few as the data needs.
                    -- sys.ensure_line_partitions (the system.partitions task) adds later years; the DEFAULT partition catches anything else.
                    FOR y IN v_first .. v_now + 2 LOOP
                        PERFORM gl.ensure_journal_line_year(y);
                    END LOOP;
                    EXECUTE 'CREATE TABLE gl.journal_line_default PARTITION OF gl.journal_line DEFAULT';
                    -- The table's triggers go back on the partitioned table, whose triggers (constraint ones too) PostgreSQL clones onto every partition.
                    FOR v_trigger IN SELECT definition FROM sys.line_partition_trigger WHERE relation = 'gl.journal_line' LOOP
                        EXECUTE v_trigger.definition;
                    END LOOP;
                END;
                $$;

                -- The copy is not a business change either: the partitioned table's triggers (and its partitions') stay quiet for it.
                ALTER TABLE gl.journal_line DISABLE TRIGGER USER;
                INSERT INTO gl.journal_line OVERRIDING SYSTEM VALUE SELECT * FROM gl.journal_line_unpartitioned;
                ALTER TABLE gl.journal_line ENABLE TRIGGER USER;
                SELECT setval(pg_get_serial_sequence('gl.journal_line', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM gl.journal_line), 1));
                DROP TABLE gl.journal_line_unpartitioned;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind, definition FROM sys.line_partition_dependent WHERE relation = 'gl.journal_line' AND kind IN ('v', 'm') ORDER BY kind DESC LOOP
                        EXECUTE format('CREATE %s %s AS %s', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name, v_dep.definition);
                    END LOOP;
                    FOR v_dep IN SELECT definition FROM sys.line_partition_dependent WHERE relation = 'gl.journal_line' AND kind = 'i' LOOP
                        EXECUTE v_dep.definition;
                    END LOOP;
                END;
                $$;

                -- pos.sale_line: the lines carry their header's company and date, then the table is rebuilt partitioned by year of sale_date.
                ALTER TABLE pos.sale_line ADD COLUMN IF NOT EXISTS company_id bigint, ADD COLUMN IF NOT EXISTS sale_date date;
                -- The backfill is not a business change: the table's own triggers (posted-row guard, balance check, audit capture) stay quiet for it.
                ALTER TABLE pos.sale_line DISABLE TRIGGER USER;
                UPDATE pos.sale_line l SET company_id = h.company_id, sale_date = h.sale_date FROM pos.sale h WHERE h.id = l.sale_id AND (l.company_id IS NULL OR l.sale_date IS NULL);
                ALTER TABLE pos.sale_line ENABLE TRIGGER USER;
                ALTER TABLE pos.sale_line ALTER COLUMN company_id SET NOT NULL, ALTER COLUMN sale_date SET NOT NULL;

                -- The table's own triggers, kept to put back on the partitioned table (constraint triggers go on each partition).
                INSERT INTO sys.line_partition_trigger (relation, trigger_name, is_constraint, definition)
                SELECT 'pos.sale_line', tgname, tgconstraint <> 0, pg_get_triggerdef(oid) FROM pg_trigger WHERE tgrelid = 'pos.sale_line'::regclass AND NOT tgisinternal
                ON CONFLICT (relation, trigger_name) DO UPDATE SET definition = EXCLUDED.definition, is_constraint = EXCLUDED.is_constraint;

                -- Views and materialised views on the table (a reporting snapshot), kept with their indexes and put back after the rebuild.
                INSERT INTO sys.line_partition_dependent (relation, name, kind, definition)
                SELECT DISTINCT 'pos.sale_line', n.nspname || '.' || c.relname, c.relkind::text, pg_get_viewdef(c.oid, true)
                FROM pg_depend d JOIN pg_rewrite r ON r.oid = d.objid JOIN pg_class c ON c.oid = r.ev_class JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE d.refobjid = 'pos.sale_line'::regclass AND c.oid <> 'pos.sale_line'::regclass AND c.relkind IN ('v', 'm')
                ON CONFLICT (relation, name) DO UPDATE SET definition = EXCLUDED.definition;
                INSERT INTO sys.line_partition_dependent (relation, name, kind, definition)
                SELECT 'pos.sale_line', i.schemaname || '.' || i.indexname, 'i', i.indexdef FROM pg_indexes i
                WHERE i.schemaname || '.' || i.tablename IN (SELECT name FROM sys.line_partition_dependent WHERE relation = 'pos.sale_line' AND kind = 'm')
                ON CONFLICT (relation, name) DO UPDATE SET definition = EXCLUDED.definition;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind FROM sys.line_partition_dependent WHERE relation = 'pos.sale_line' AND kind IN ('v', 'm') LOOP
                        EXECUTE format('DROP %s IF EXISTS %s CASCADE', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name);
                    END LOOP;
                END;
                $$;

                ALTER TABLE pos.sale_line RENAME TO sale_line_unpartitioned;
                ALTER TABLE pos.sale_line_unpartitioned RENAME CONSTRAINT pk_sale_line TO pk_sale_line_unpartitioned;
                ALTER INDEX pos.ix_sale_line_sale_id RENAME TO ix_sale_line_sale_id_unpartitioned;

                CREATE TABLE pos.sale_line (LIKE pos.sale_line_unpartitioned INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE INCLUDING CONSTRAINTS)
                    PARTITION BY RANGE (sale_date);
                ALTER TABLE pos.sale_line ADD CONSTRAINT pk_sale_line PRIMARY KEY (id, sale_date);
                ALTER TABLE pos.sale_line ADD CONSTRAINT fk_sale_line_sales_sale_id FOREIGN KEY (sale_id) REFERENCES pos.sale (id) ON DELETE CASCADE;
                CREATE INDEX ix_sale_line_sale_id ON pos.sale_line (sale_id);
                CREATE INDEX ix_sale_line_company_id_sale_date ON pos.sale_line (company_id, sale_date);

                -- A year's partition (the partitioned table's triggers, constraint triggers included, are cloned onto it by PostgreSQL).
                CREATE OR REPLACE FUNCTION pos.ensure_sale_line_year(p_year integer) RETURNS void
                LANGUAGE plpgsql AS $$
                DECLARE
                    v_name text := format('sale_line_y%s', p_year);
                BEGIN
                    IF to_regclass(format('pos.%I', v_name)) IS NULL THEN
                        EXECUTE format('CREATE TABLE pos.%I PARTITION OF pos.sale_line FOR VALUES FROM (%L) TO (%L)', v_name, make_date(p_year, 1, 1), make_date(p_year + 1, 1, 1));
                    END IF;
                END;
                $$;

                DO $$
                DECLARE
                    v_now integer := extract(year FROM current_date)::integer;
                    v_first integer := LEAST(v_now - 1, COALESCE((SELECT extract(year FROM min(sale_date))::integer FROM pos.sale_line_unpartitioned), v_now));
                    v_trigger record;
                BEGIN
                    -- Only the years in use and two ahead: a lookup by header id probes every partition, so there are as few as the data needs.
                    -- sys.ensure_line_partitions (the system.partitions task) adds later years; the DEFAULT partition catches anything else.
                    FOR y IN v_first .. v_now + 2 LOOP
                        PERFORM pos.ensure_sale_line_year(y);
                    END LOOP;
                    EXECUTE 'CREATE TABLE pos.sale_line_default PARTITION OF pos.sale_line DEFAULT';
                    -- The table's triggers go back on the partitioned table, whose triggers (constraint ones too) PostgreSQL clones onto every partition.
                    FOR v_trigger IN SELECT definition FROM sys.line_partition_trigger WHERE relation = 'pos.sale_line' LOOP
                        EXECUTE v_trigger.definition;
                    END LOOP;
                END;
                $$;

                -- The copy is not a business change either: the partitioned table's triggers (and its partitions') stay quiet for it.
                ALTER TABLE pos.sale_line DISABLE TRIGGER USER;
                INSERT INTO pos.sale_line OVERRIDING SYSTEM VALUE SELECT * FROM pos.sale_line_unpartitioned;
                ALTER TABLE pos.sale_line ENABLE TRIGGER USER;
                SELECT setval(pg_get_serial_sequence('pos.sale_line', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM pos.sale_line), 1));
                DROP TABLE pos.sale_line_unpartitioned;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind, definition FROM sys.line_partition_dependent WHERE relation = 'pos.sale_line' AND kind IN ('v', 'm') ORDER BY kind DESC LOOP
                        EXECUTE format('CREATE %s %s AS %s', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name, v_dep.definition);
                    END LOOP;
                    FOR v_dep IN SELECT definition FROM sys.line_partition_dependent WHERE relation = 'pos.sale_line' AND kind = 'i' LOOP
                        EXECUTE v_dep.definition;
                    END LOOP;
                END;
                $$;

                -- pay.payslip_line: the lines carry their header's company and date, then the table is rebuilt partitioned by year of pay_date.
                ALTER TABLE pay.payslip_line ADD COLUMN IF NOT EXISTS company_id bigint, ADD COLUMN IF NOT EXISTS pay_date date;
                -- The backfill is not a business change: the table's own triggers (posted-row guard, balance check, audit capture) stay quiet for it.
                ALTER TABLE pay.payslip_line DISABLE TRIGGER USER;
                UPDATE pay.payslip_line l SET company_id = h.company_id, pay_date = h.pay_date FROM pay.payslip h WHERE h.id = l.payslip_id AND (l.company_id IS NULL OR l.pay_date IS NULL);
                ALTER TABLE pay.payslip_line ENABLE TRIGGER USER;
                ALTER TABLE pay.payslip_line ALTER COLUMN company_id SET NOT NULL, ALTER COLUMN pay_date SET NOT NULL;

                -- The table's own triggers, kept to put back on the partitioned table (constraint triggers go on each partition).
                INSERT INTO sys.line_partition_trigger (relation, trigger_name, is_constraint, definition)
                SELECT 'pay.payslip_line', tgname, tgconstraint <> 0, pg_get_triggerdef(oid) FROM pg_trigger WHERE tgrelid = 'pay.payslip_line'::regclass AND NOT tgisinternal
                ON CONFLICT (relation, trigger_name) DO UPDATE SET definition = EXCLUDED.definition, is_constraint = EXCLUDED.is_constraint;

                -- Views and materialised views on the table (a reporting snapshot), kept with their indexes and put back after the rebuild.
                INSERT INTO sys.line_partition_dependent (relation, name, kind, definition)
                SELECT DISTINCT 'pay.payslip_line', n.nspname || '.' || c.relname, c.relkind::text, pg_get_viewdef(c.oid, true)
                FROM pg_depend d JOIN pg_rewrite r ON r.oid = d.objid JOIN pg_class c ON c.oid = r.ev_class JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE d.refobjid = 'pay.payslip_line'::regclass AND c.oid <> 'pay.payslip_line'::regclass AND c.relkind IN ('v', 'm')
                ON CONFLICT (relation, name) DO UPDATE SET definition = EXCLUDED.definition;
                INSERT INTO sys.line_partition_dependent (relation, name, kind, definition)
                SELECT 'pay.payslip_line', i.schemaname || '.' || i.indexname, 'i', i.indexdef FROM pg_indexes i
                WHERE i.schemaname || '.' || i.tablename IN (SELECT name FROM sys.line_partition_dependent WHERE relation = 'pay.payslip_line' AND kind = 'm')
                ON CONFLICT (relation, name) DO UPDATE SET definition = EXCLUDED.definition;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind FROM sys.line_partition_dependent WHERE relation = 'pay.payslip_line' AND kind IN ('v', 'm') LOOP
                        EXECUTE format('DROP %s IF EXISTS %s CASCADE', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name);
                    END LOOP;
                END;
                $$;

                ALTER TABLE pay.payslip_line RENAME TO payslip_line_unpartitioned;
                ALTER TABLE pay.payslip_line_unpartitioned RENAME CONSTRAINT pk_payslip_line TO pk_payslip_line_unpartitioned;
                ALTER INDEX pay.ix_payslip_line_payslip_id RENAME TO ix_payslip_line_payslip_id_unpartitioned;

                CREATE TABLE pay.payslip_line (LIKE pay.payslip_line_unpartitioned INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE INCLUDING CONSTRAINTS)
                    PARTITION BY RANGE (pay_date);
                ALTER TABLE pay.payslip_line ADD CONSTRAINT pk_payslip_line PRIMARY KEY (id, pay_date);
                ALTER TABLE pay.payslip_line ADD CONSTRAINT fk_payslip_line_payslips_payslip_id FOREIGN KEY (payslip_id) REFERENCES pay.payslip (id) ON DELETE CASCADE;
                CREATE INDEX ix_payslip_line_payslip_id ON pay.payslip_line (payslip_id);
                CREATE INDEX ix_payslip_line_company_id_pay_date ON pay.payslip_line (company_id, pay_date);

                -- A year's partition (the partitioned table's triggers, constraint triggers included, are cloned onto it by PostgreSQL).
                CREATE OR REPLACE FUNCTION pay.ensure_payslip_line_year(p_year integer) RETURNS void
                LANGUAGE plpgsql AS $$
                DECLARE
                    v_name text := format('payslip_line_y%s', p_year);
                BEGIN
                    IF to_regclass(format('pay.%I', v_name)) IS NULL THEN
                        EXECUTE format('CREATE TABLE pay.%I PARTITION OF pay.payslip_line FOR VALUES FROM (%L) TO (%L)', v_name, make_date(p_year, 1, 1), make_date(p_year + 1, 1, 1));
                    END IF;
                END;
                $$;

                DO $$
                DECLARE
                    v_now integer := extract(year FROM current_date)::integer;
                    v_first integer := LEAST(v_now - 1, COALESCE((SELECT extract(year FROM min(pay_date))::integer FROM pay.payslip_line_unpartitioned), v_now));
                    v_trigger record;
                BEGIN
                    -- Only the years in use and two ahead: a lookup by header id probes every partition, so there are as few as the data needs.
                    -- sys.ensure_line_partitions (the system.partitions task) adds later years; the DEFAULT partition catches anything else.
                    FOR y IN v_first .. v_now + 2 LOOP
                        PERFORM pay.ensure_payslip_line_year(y);
                    END LOOP;
                    EXECUTE 'CREATE TABLE pay.payslip_line_default PARTITION OF pay.payslip_line DEFAULT';
                    -- The table's triggers go back on the partitioned table, whose triggers (constraint ones too) PostgreSQL clones onto every partition.
                    FOR v_trigger IN SELECT definition FROM sys.line_partition_trigger WHERE relation = 'pay.payslip_line' LOOP
                        EXECUTE v_trigger.definition;
                    END LOOP;
                END;
                $$;

                -- The copy is not a business change either: the partitioned table's triggers (and its partitions') stay quiet for it.
                ALTER TABLE pay.payslip_line DISABLE TRIGGER USER;
                INSERT INTO pay.payslip_line OVERRIDING SYSTEM VALUE SELECT * FROM pay.payslip_line_unpartitioned;
                ALTER TABLE pay.payslip_line ENABLE TRIGGER USER;
                SELECT setval(pg_get_serial_sequence('pay.payslip_line', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM pay.payslip_line), 1));
                DROP TABLE pay.payslip_line_unpartitioned;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind, definition FROM sys.line_partition_dependent WHERE relation = 'pay.payslip_line' AND kind IN ('v', 'm') ORDER BY kind DESC LOOP
                        EXECUTE format('CREATE %s %s AS %s', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name, v_dep.definition);
                    END LOOP;
                    FOR v_dep IN SELECT definition FROM sys.line_partition_dependent WHERE relation = 'pay.payslip_line' AND kind = 'i' LOOP
                        EXECUTE v_dep.definition;
                    END LOOP;
                END;
                $$;

                -- One call makes a year's partition on every partitioned line table (the system.partitions task calls it for next year).
                CREATE OR REPLACE FUNCTION sys.ensure_line_partitions(p_year integer) RETURNS void
                LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM gl.ensure_journal_line_year(p_year);
                    PERFORM pos.ensure_sale_line_year(p_year);
                    PERFORM pay.ensure_payslip_line_year(p_year);
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS sys.ensure_line_partitions(integer);

                -- gl.journal_line: back to one plain table (the company and date columns stay; they are harmless).
                CREATE TABLE gl.journal_line_plain (LIKE gl.journal_line INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE INCLUDING CONSTRAINTS);
                ALTER TABLE gl.journal_line DISABLE TRIGGER USER;
                INSERT INTO gl.journal_line_plain OVERRIDING SYSTEM VALUE SELECT * FROM gl.journal_line;
                DROP TABLE gl.journal_line CASCADE;
                DROP FUNCTION IF EXISTS gl.ensure_journal_line_year(integer);
                ALTER TABLE gl.journal_line_plain RENAME TO journal_line;
                ALTER TABLE gl.journal_line ADD CONSTRAINT pk_journal_line PRIMARY KEY (id);
                ALTER TABLE gl.journal_line ADD CONSTRAINT fk_journal_line_journals_journal_entry_id FOREIGN KEY (journal_entry_id) REFERENCES gl.journal (id) ON DELETE CASCADE;
                CREATE INDEX ix_journal_line_account_code_cost_centre_department ON gl.journal_line (account_code, cost_centre, department);
                CREATE INDEX ix_journal_line_fund ON gl.journal_line (fund) WHERE fund IS NOT NULL;
                CREATE INDEX ix_journal_line_journal_entry_id ON gl.journal_line (journal_entry_id);
                CREATE INDEX ix_journal_line_project ON gl.journal_line (project) WHERE project IS NOT NULL;
                SELECT setval(pg_get_serial_sequence('gl.journal_line', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM gl.journal_line), 1));
                DO $$
                DECLARE
                    v_trigger record;
                BEGIN
                    FOR v_trigger IN SELECT definition FROM sys.line_partition_trigger WHERE relation = 'gl.journal_line' LOOP
                        EXECUTE v_trigger.definition;
                    END LOOP;
                END;
                $$;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind, definition FROM sys.line_partition_dependent WHERE relation = 'gl.journal_line' AND kind IN ('v', 'm') ORDER BY kind DESC LOOP
                        EXECUTE format('CREATE %s %s AS %s', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name, v_dep.definition);
                    END LOOP;
                    FOR v_dep IN SELECT definition FROM sys.line_partition_dependent WHERE relation = 'gl.journal_line' AND kind = 'i' LOOP
                        EXECUTE v_dep.definition;
                    END LOOP;
                END;
                $$;

                -- pos.sale_line: back to one plain table (the company and date columns stay; they are harmless).
                CREATE TABLE pos.sale_line_plain (LIKE pos.sale_line INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE INCLUDING CONSTRAINTS);
                ALTER TABLE pos.sale_line DISABLE TRIGGER USER;
                INSERT INTO pos.sale_line_plain OVERRIDING SYSTEM VALUE SELECT * FROM pos.sale_line;
                DROP TABLE pos.sale_line CASCADE;
                DROP FUNCTION IF EXISTS pos.ensure_sale_line_year(integer);
                ALTER TABLE pos.sale_line_plain RENAME TO sale_line;
                ALTER TABLE pos.sale_line ADD CONSTRAINT pk_sale_line PRIMARY KEY (id);
                ALTER TABLE pos.sale_line ADD CONSTRAINT fk_sale_line_sales_sale_id FOREIGN KEY (sale_id) REFERENCES pos.sale (id) ON DELETE CASCADE;
                CREATE INDEX ix_sale_line_sale_id ON pos.sale_line (sale_id);
                SELECT setval(pg_get_serial_sequence('pos.sale_line', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM pos.sale_line), 1));
                DO $$
                DECLARE
                    v_trigger record;
                BEGIN
                    FOR v_trigger IN SELECT definition FROM sys.line_partition_trigger WHERE relation = 'pos.sale_line' LOOP
                        EXECUTE v_trigger.definition;
                    END LOOP;
                END;
                $$;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind, definition FROM sys.line_partition_dependent WHERE relation = 'pos.sale_line' AND kind IN ('v', 'm') ORDER BY kind DESC LOOP
                        EXECUTE format('CREATE %s %s AS %s', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name, v_dep.definition);
                    END LOOP;
                    FOR v_dep IN SELECT definition FROM sys.line_partition_dependent WHERE relation = 'pos.sale_line' AND kind = 'i' LOOP
                        EXECUTE v_dep.definition;
                    END LOOP;
                END;
                $$;

                -- pay.payslip_line: back to one plain table (the company and date columns stay; they are harmless).
                CREATE TABLE pay.payslip_line_plain (LIKE pay.payslip_line INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE INCLUDING CONSTRAINTS);
                ALTER TABLE pay.payslip_line DISABLE TRIGGER USER;
                INSERT INTO pay.payslip_line_plain OVERRIDING SYSTEM VALUE SELECT * FROM pay.payslip_line;
                DROP TABLE pay.payslip_line CASCADE;
                DROP FUNCTION IF EXISTS pay.ensure_payslip_line_year(integer);
                ALTER TABLE pay.payslip_line_plain RENAME TO payslip_line;
                ALTER TABLE pay.payslip_line ADD CONSTRAINT pk_payslip_line PRIMARY KEY (id);
                ALTER TABLE pay.payslip_line ADD CONSTRAINT fk_payslip_line_payslips_payslip_id FOREIGN KEY (payslip_id) REFERENCES pay.payslip (id) ON DELETE CASCADE;
                CREATE INDEX ix_payslip_line_payslip_id ON pay.payslip_line (payslip_id);
                SELECT setval(pg_get_serial_sequence('pay.payslip_line', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM pay.payslip_line), 1));
                DO $$
                DECLARE
                    v_trigger record;
                BEGIN
                    FOR v_trigger IN SELECT definition FROM sys.line_partition_trigger WHERE relation = 'pay.payslip_line' LOOP
                        EXECUTE v_trigger.definition;
                    END LOOP;
                END;
                $$;
                DO $$
                DECLARE
                    v_dep record;
                BEGIN
                    FOR v_dep IN SELECT name, kind, definition FROM sys.line_partition_dependent WHERE relation = 'pay.payslip_line' AND kind IN ('v', 'm') ORDER BY kind DESC LOOP
                        EXECUTE format('CREATE %s %s AS %s', CASE v_dep.kind WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'VIEW' END, v_dep.name, v_dep.definition);
                    END LOOP;
                    FOR v_dep IN SELECT definition FROM sys.line_partition_dependent WHERE relation = 'pay.payslip_line' AND kind = 'i' LOOP
                        EXECUTE v_dep.definition;
                    END LOOP;
                END;
                $$;
                DROP TABLE sys.line_partition_trigger;
                DROP TABLE sys.line_partition_dependent;
                """);
        }
    }
}
