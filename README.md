# One4All ERP

A Windows-desktop ERP (Sage 200-style) with a single codebase deployable to supermarkets,
retail, hospitality, butchery, bulk materials, construction, schools, councils/municipalities
and housing/land development - plus cross-cutting **budgeting** and **payroll**. Multi-company,
multi-currency (IAS 21), IFRS-correct accounting core.

> Full contract: [`docs/BRIEF.md`](docs/BRIEF.md). Progress log: [`docs/PROGRESS.md`](docs/PROGRESS.md).

## Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 (`net10.0`; WPF `net10.0-windows`) - see [ADR-0001](docs/decisions/ADR-0001-net10-instead-of-net8.md) |
| Desktop UI | WPF + CommunityToolkit.Mvvm (Avalonia-port-friendly: no business logic in code-behind) |
| App server | ASP.NET Core Web API (`One4All.Server`); embedded in-process for single-PC installs |
| Database | PostgreSQL 18 dev / 16+ supported - [ADR-0002](docs/decisions/ADR-0002-postgresql-18.md) |
| Data access | EF Core 10 (write model) + Dapper (reporting / set-based reads) |
| Tests | xUnit + FluentAssertions (7.x, last free version) + Testcontainers (CI) |

## Solution layout

```
One4All.slnx
├─ src/
│  ├─ One4All.Core/            # Entities, value objects (Money, CurrencyCode), Result<T>. No I/O.
│  ├─ One4All.Application/     # CQRS handlers, DTOs, validators, ports
│  ├─ One4All.Infrastructure/  # EF Core, Npgsql, Dapper, raw-SQL runner, repositories
│  ├─ One4All.Server/          # ASP.NET Core Web API host
│  └─ One4All.Desktop/         # WPF shell + module UIs + POS
├─ tests/
│  ├─ One4All.UnitTests/
│  ├─ One4All.IntegrationTests/   # Postgres (Testcontainers on CI, local PG fallback)
│  └─ One4All.AccountingTests/    # Balance / reconcile / revalue suite
├─ db/sql/{views,functions,triggers,seed}/
└─ docs/{decisions,reports,demos,coa}/
```

## Getting started

```bash
dotnet build One4All.slnx
dotnet test  tests/One4All.UnitTests/One4All.UnitTests.csproj
```

For database work, set the connection string (never commit a real password):

```bash
export ONE4ALL_CONNECTION="Host=localhost;Port=5432;Database=one4all;Username=postgres;Password=****"
```

## Build order

Phased delivery per brief §16. Current phase: **Phase 0 - Foundations** (see `docs/PROGRESS.md`).
