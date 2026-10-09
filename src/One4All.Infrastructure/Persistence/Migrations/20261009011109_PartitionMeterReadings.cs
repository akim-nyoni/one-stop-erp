using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartitionMeterReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // mun.meter_reading becomes partitioned by year of reading_date (brief §11: partitioning on mun.meter_reading); its primary key holds the
            // partition column, so it becomes (id, reading_date); ids stay unique through the identity. Nothing references a reading by key.
            migrationBuilder.Sql("""
                ALTER TABLE mun.meter_reading RENAME TO meter_reading_unpartitioned;
                ALTER TABLE mun.meter_reading_unpartitioned RENAME CONSTRAINT pk_meter_reading TO pk_meter_reading_unpartitioned;
                ALTER INDEX mun.ix_meter_reading_company_id_meter_id_reading_date RENAME TO ix_meter_reading_unpartitioned_meter;

                CREATE TABLE mun.meter_reading (LIKE mun.meter_reading_unpartitioned INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE)
                    PARTITION BY RANGE (reading_date);
                ALTER TABLE mun.meter_reading ADD CONSTRAINT pk_meter_reading PRIMARY KEY (id, reading_date);
                CREATE INDEX ix_meter_reading_company_id_meter_id_reading_date ON mun.meter_reading (company_id, meter_id, reading_date);

                CREATE OR REPLACE FUNCTION mun.ensure_meter_reading_year(p_year integer) RETURNS void
                LANGUAGE plpgsql AS $$
                DECLARE
                    v_name text := format('meter_reading_y%s', p_year);
                BEGIN
                    IF to_regclass(format('mun.%I', v_name)) IS NULL THEN
                        EXECUTE format('CREATE TABLE mun.%I PARTITION OF mun.meter_reading FOR VALUES FROM (%L) TO (%L)', v_name,
                            make_date(p_year, 1, 1), make_date(p_year + 1, 1, 1));
                    END IF;
                END;
                $$;

                DO $$
                DECLARE
                    v_first integer := LEAST(2020, COALESCE((SELECT extract(year FROM min(reading_date))::integer FROM mun.meter_reading_unpartitioned), 2020));
                BEGIN
                    FOR y IN v_first .. 2040 LOOP
                        PERFORM mun.ensure_meter_reading_year(y);
                    END LOOP;
                END;
                $$;
                CREATE TABLE mun.meter_reading_default PARTITION OF mun.meter_reading DEFAULT;

                INSERT INTO mun.meter_reading OVERRIDING SYSTEM VALUE SELECT * FROM mun.meter_reading_unpartitioned;
                SELECT setval(pg_get_serial_sequence('mun.meter_reading', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM mun.meter_reading), 1));
                DROP TABLE mun.meter_reading_unpartitioned;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("""
                CREATE TABLE mun.meter_reading_plain (LIKE mun.meter_reading INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY INCLUDING STORAGE);
                INSERT INTO mun.meter_reading_plain OVERRIDING SYSTEM VALUE SELECT * FROM mun.meter_reading;
                SELECT setval(pg_get_serial_sequence('mun.meter_reading_plain', 'id'), GREATEST((SELECT COALESCE(max(id), 0) FROM mun.meter_reading_plain), 1));
                DROP TABLE mun.meter_reading;
                DROP FUNCTION IF EXISTS mun.ensure_meter_reading_year(integer);
                ALTER TABLE mun.meter_reading_plain RENAME TO meter_reading;
                ALTER TABLE mun.meter_reading ADD CONSTRAINT pk_meter_reading PRIMARY KEY (id);
                CREATE INDEX ix_meter_reading_company_id_meter_id_reading_date ON mun.meter_reading (company_id, meter_id, reading_date);
                """);
        }
    }
}
