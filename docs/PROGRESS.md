# One4All ERP — Progress

_Running log of what's done, what's stubbed, and what's next. Updated every phase._

## Legend
✅ done & tested · 🟡 in progress / stubbed · ⬜ not started

---

## Phase 0 — Foundations
| Item | Status | Notes |
|---|---|---|
| Repo, `.gitignore`, `.editorconfig`, `Directory.Build.props`, `global.json` | ✅ | net10.0, warnings-as-errors on Core/Application |
| `docs/BRIEF.md` (contract) | ✅ | Copied verbatim from the build brief |
| ADRs 0001–0003 (net10, PG18, name One4All) | ✅ | |
| Solution + project skeleton | ✅ | Core/Application/Infrastructure/Server/Desktop + 3 test projects; builds clean, 0 warnings |
| Core primitives — `Result<T>`, `Error`, `Entity`, `AuditableEntity`, `DomainEvent` | ✅ | 26 unit tests green |
| Money / CurrencyCode / allocation value objects | ✅ | Lossless allocation, currency-mismatch guard, banker's rounding |
| EF Core + Npgsql `One4AllDbContext` (xmin concurrency, numeric(19,4) default, schema constants) | ✅ | Design-time factory for migrations |
| Raw-SQL migration runner (idempotent, hash-tracked) | ✅ | `ISqlScriptRunner` / `SqlScriptRunner` for views/functions/triggers/seed |
| Testcontainers harness (fallback to local PG — no Docker on dev box) | 🟡 | CI uses postgres:16 service; local harness lands with first integration test in Phase 1 |
| CI build workflow | ✅ | `.github/workflows/ci.yml` — build + unit/accounting/integration tests |

## Phase 1 — System Manager + Security + Licensing
_Not started. Companies, branches, users, roles, groups, permission grid, scopes, audit trail (both layers), periods, currencies & rates, number sequences, Licensing.Core + Generator, licence enforcement, WPF shell._

---

## Decisions / deviations from brief
- **.NET 10** not .NET 8 (ADR-0001) — only SDK on the machine.
- **PostgreSQL 18** dev, min supported 16 (ADR-0002).
- **Product name One4All** not Meridian (ADR-0003).
- **No Docker** on the dev machine → integration tests fall back to the local PG18 instance; Testcontainers path kept for CI.

## Open questions for the product owner
- Local `postgres` password for `appsettings.Development.json` (not committed).
