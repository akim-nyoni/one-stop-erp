using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database performance (brief §13): trigram GIN indexes for typo-tolerant search on names and codes, BRIN indexes on the large
    /// date-ordered tables, and materialised snapshots for the heavy reporting aggregates (aged debt, stock valuation, sales by day),
    /// each with a unique index so it refreshes concurrently, and a refresh log for the "as at" stamp.
    /// </summary>
    public partial class ReportingSnapshotsAndSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
                CREATE SCHEMA IF NOT EXISTS rpt;

                CREATE INDEX IF NOT EXISTS ix_item_description_trgm ON inv.item USING gin (description gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_item_code_trgm ON inv.item USING gin (code gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_customer_name_trgm ON ar.customer USING gin (name gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_supplier_name_trgm ON ap.supplier USING gin (name gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_lead_name_trgm ON crm.lead USING gin (name gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_lead_organisation_trgm ON crm.lead USING gin (organisation gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_account_name_trgm ON gl.account USING gin (name gin_trgm_ops);

                CREATE INDEX IF NOT EXISTS ix_journal_posting_date_brin ON gl.journal USING brin (posting_date);
                CREATE INDEX IF NOT EXISTS ix_open_item_document_date_brin ON trd.open_item USING brin (document_date);
                CREATE INDEX IF NOT EXISTS ix_stock_movement_date_brin ON inv.stock_movement USING brin (date);
                CREATE INDEX IF NOT EXISTS ix_sale_date_brin ON pos.sale USING brin (sale_date);

                CREATE TABLE sys.snapshot_refresh (
                    name varchar(60) PRIMARY KEY,
                    refreshed_on_utc timestamptz NOT NULL,
                    duration_ms integer NOT NULL,
                    row_count bigint NOT NULL);

                CREATE MATERIALIZED VIEW rpt.aged_debt AS
                SELECT o.company_id, o.trader_id AS customer_id, c.code AS customer_code, c.name AS customer_name, current_date AS as_at,
                       sum(CASE WHEN current_date - coalesce(o.due_date, o.document_date) <= 0 THEN a.amount ELSE 0 END) AS current_due,
                       sum(CASE WHEN current_date - coalesce(o.due_date, o.document_date) BETWEEN 1 AND 30 THEN a.amount ELSE 0 END) AS days_1_30,
                       sum(CASE WHEN current_date - coalesce(o.due_date, o.document_date) BETWEEN 31 AND 60 THEN a.amount ELSE 0 END) AS days_31_60,
                       sum(CASE WHEN current_date - coalesce(o.due_date, o.document_date) BETWEEN 61 AND 90 THEN a.amount ELSE 0 END) AS days_61_90,
                       sum(CASE WHEN current_date - coalesce(o.due_date, o.document_date) > 90 THEN a.amount ELSE 0 END) AS days_over_90,
                       sum(a.amount) AS total
                FROM trd.open_item o
                CROSS JOIN LATERAL (SELECT CASE WHEN o.sign = 1 THEN o.outstanding_base ELSE -o.outstanding_base END AS amount) a
                LEFT JOIN ar.customer c ON c.id = o.trader_id
                WHERE o.ledger = 1 AND o.outstanding_base <> 0
                GROUP BY o.company_id, o.trader_id, c.code, c.name;
                CREATE UNIQUE INDEX ux_aged_debt ON rpt.aged_debt (company_id, customer_id);

                CREATE MATERIALIZED VIEW rpt.stock_valuation AS
                SELECT b.company_id, b.warehouse_id, w.code AS warehouse_code, coalesce(i.category, '') AS category,
                       count(*) AS items, sum(b.quantity_on_hand) AS quantity, sum(b.total_value) AS value
                FROM inv.stock_balance b
                JOIN inv.item i ON i.id = b.item_id
                JOIN inv.warehouse w ON w.id = b.warehouse_id
                WHERE b.quantity_on_hand <> 0 OR b.total_value <> 0
                GROUP BY b.company_id, b.warehouse_id, w.code, coalesce(i.category, '');
                CREATE UNIQUE INDEX ux_stock_valuation ON rpt.stock_valuation (company_id, warehouse_id, category);

                CREATE MATERIALIZED VIEW rpt.sales_by_day AS
                SELECT j.company_id, j.posting_date, coalesce(l.department, '') AS department,
                       sum(l.base_credit - l.base_debit) AS revenue, count(*) AS lines
                FROM gl.journal_line l
                JOIN gl.journal j ON j.id = l.journal_entry_id
                JOIN gl.account a ON a.company_id = j.company_id AND a.account_code = l.account_code
                WHERE j.status IN (2, 3) AND a.type = 4
                GROUP BY j.company_id, j.posting_date, coalesce(l.department, '');
                CREATE UNIQUE INDEX ux_sales_by_day ON rpt.sales_by_day (company_id, posting_date, department);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP MATERIALIZED VIEW IF EXISTS rpt.sales_by_day;
                DROP MATERIALIZED VIEW IF EXISTS rpt.stock_valuation;
                DROP MATERIALIZED VIEW IF EXISTS rpt.aged_debt;
                DROP TABLE IF EXISTS sys.snapshot_refresh;
                DROP INDEX IF EXISTS pos.ix_sale_date_brin;
                DROP INDEX IF EXISTS inv.ix_stock_movement_date_brin;
                DROP INDEX IF EXISTS trd.ix_open_item_document_date_brin;
                DROP INDEX IF EXISTS gl.ix_journal_posting_date_brin;
                DROP INDEX IF EXISTS gl.ix_account_name_trgm;
                DROP INDEX IF EXISTS crm.ix_lead_organisation_trgm;
                DROP INDEX IF EXISTS crm.ix_lead_name_trgm;
                DROP INDEX IF EXISTS ap.ix_supplier_name_trgm;
                DROP INDEX IF EXISTS ar.ix_customer_name_trgm;
                DROP INDEX IF EXISTS inv.ix_item_code_trgm;
                DROP INDEX IF EXISTS inv.ix_item_description_trgm;
                DROP SCHEMA IF EXISTS rpt;
                """);
        }
    }
}
