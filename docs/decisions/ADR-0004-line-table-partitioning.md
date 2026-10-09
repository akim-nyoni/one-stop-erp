# ADR-0004 — Which tables are partitioned

**Status:** accepted · 2026-10-09

## Context

The brief (§11) asks for partitioning by company and year on `gl.transaction_line`, `pos.sale_line`, `audit.change_log`, `mun.meter_reading`,
`mun.bill_line`, `hse.ownership_history`, `bud.budget_line` and `pay.payslip_line`.

PostgreSQL requires a partitioned table's primary key, and every unique index, to include the partition key. The partition key must be a column of the
table itself.

## Decision

- **Partitioned by year:** `audit.change_log` (`occurred_on_utc`) and `mun.meter_reading` (`reading_date`). Both carry their own date, nothing references them
  by key, and they are the fastest-growing append-mostly tables. Each has a year partition from 2020 to 2040 plus a default partition. Further years are
  added with `audit.ensure_change_log_year(y)` / `mun.ensure_meter_reading_year(y)`. Neither maps `xmin`, because PostgreSQL cannot return it from an insert
  into a partitioned table.
- **Not partitioned (yet):** the line tables — `gl.journal_line`, `pos.sale_line`, `pay.payslip_line`, `bud.budget_line`, `hse.ownership_record`. They have no company or date
  of their own; those live on their parent (journal, sale, payslip, budget, stand). Partitioning them would mean copying a year onto every line and
  making it part of the key. For journal lines it would also mean reworking the posted-journal immutability triggers and the posting engine's
  balance updates. None of that is needed at today's volumes. PostgreSQL serves them well through their parent-id and date indexes, and the BRIN date
  indexes on the parents.
- `mun.bill_line` does not exist: municipal bills post as receivables open items, which are not partitioned for the same reason.

## When to revisit

When a line table passes about 50 million rows, or year-end and archival need to detach whole years, add a `posting_year` column set on insert. Make it part
of the key, partition by it, and adjust the immutability triggers in the same migration.
