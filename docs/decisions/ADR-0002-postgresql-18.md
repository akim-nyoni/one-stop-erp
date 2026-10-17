# ADR-0002 - PostgreSQL 18 as the development database

- **Status:** Accepted
- **Date:** 2026-09-14

## Context

The brief (§2.2, §9) targets **PostgreSQL 16**. The build machine has **PostgreSQL 18** installed and running as the `postgresql-x64-18` service. Nothing the brief requires (schemas, `numeric`, partitioning, `pg_trgm`, `unaccent`, triggers, `jsonb`, materialised views, BRIN, partial indexes) changed incompatibly between 16 and 18.

## Decision

Develop against the local **PostgreSQL 18** instance. Keep all SQL to features available in PG16+ so a customer on 16 is fully supported. `Npgsql` and EF Core `UseNpgsql` are version-agnostic across this range.

Development connection string (stored only in `appsettings.Development.json`, never committed with a production password):

```
Host=localhost;Port=5432;Database=one4all;Username=postgres;Password=****
```

The database name is **`one4all`** (was `meridian` in the brief) to match ADR-0003.

## Consequences

- The minimum supported server is documented as **PostgreSQL 16**; CI/test should run against 16 to catch accidental use of newer syntax.
- The actual local `postgres` password must be supplied by the developer in `appsettings.Development.json`; it is not known to the repo.
