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

## Phase 1 — System Manager + Security + Licensing + Shell
| Increment | Status | Notes |
|---|---|---|
| **1A** Security domain (Core) | ✅ | `OperationFlags` grid, `PermissionCatalogue` (incl. full Housing/Payroll/Budget sets), `PermissionResolution` (role union + Grant/Deny overrides, Deny wins), SoD matrix, `User`/`Role`/`Group`/scopes/overrides — 43 unit tests green |
| **1B** `sys`/`sec`/`audit` EF config + first migration | ✅ | 20 tables applied to PG18; snake_case naming; Company/Branch/Currency+denoms/ExchangeRate/FiscalYear+Period(per-module status)/NumberSequence; User/Role/Group/scopes/overrides + Permission; audit change-log with hash chain. Permission seeder (169 permissions, idempotent). 4 integration tests green against live PG |
| **1D** Application/Infrastructure services | ✅ | Argon2id hasher, PermissionService (role+group+override resolution from DB), AuthenticationService (lockout, PIN), CurrentUserContext, LicenceService (install/reload/gating from `lic` table + trusted keys), AdministratorSeeder (default admin), and the EF **audit interceptor** (auto hash-chained change log + created/modified stamping). Fixed two tamper-evidence bugs (µs timestamp precision, jsonb re-normalization). 8 integration tests green |
| **1C** Licensing.Core (Ed25519) + Generator | ✅ | Ed25519 sign/verify (BouncyCastle), canonical-JSON payload, compact token, PBKDF2→AES-GCM protected private key, fuzzy ≥3/4 machine fingerprint, clock-rollback detection, validator (grace/read-only/hard-stop + per-module gating & limits). Generator CLI (keygen/issue/verify/machineid) — end-to-end smoke test passes. 12 licensing unit tests green |
| **1D** Security/licence application services | ⬜ | Argon2id auth, permission evaluation, licence gating |
| **1E** WPF shell (Sage Evolution layout) | ✅ | Embedded-mode host (migrate + seed + licence install on startup); login (Argon2 auth) → shell with menu bar, toolbar, left System Tree + module icon grid built at runtime from **licensed + permitted** modules; licence/user status bar. CommunityToolkit.Mvvm, no business logic in code-behind. Verified rendering (screenshot). Permission-grid editor UI deferred to a follow-up. |

**Phase 1 status:** functionally complete — the shell logs in, reads the licence, and shows only licensed + permitted modules (brief §18 item 5). Remaining polish carried forward: the back-office **permission-grid editor** screen, JWT for the API path, and Windows WMI machine-fingerprint provider.

---

## Phase 2 — Accounting Core
| Increment | Status | Notes |
|---|---|---|
| **2A** Chart of accounts | ✅ | `LedgerAccount` (composite AccountCode+CostCentre+Department via `AccountKey`; account types + normal-balance rules; `ControlAccountType`; manual-journal lock), CostCentre/Department dimensions; EF config + migration |
| **2B** Posting engine | ✅ | Central non-bypassable `IPostingEngine`: enforces **Dr=Cr in base + per transaction currency**, open-period check (per-module), account validity/active, control-account manual-journal block, currency restriction, FX rate lookup, gapless journal numbering, and `gl.period_balance` update — all in one transaction. `JournalEntry`/`JournalLine`/`GlPeriodBalance`. **7 accounting tests green** (balanced posts + TB nets to zero, unbalanced rejected, control-account block, sub-ledger posting, closed-period rejected, multi-currency, sequential numbering) |
| **2C** Period control + year-end | ✅ | `IPeriodService` (open/soft-close/permanently-close per module, one-way lifecycle enforced), `IYearEndService` (validates, posts P&L closing journal to retained earnings, opens next year, permanently closes prior year). Year-end test green: profit → retained earnings, next year opened, prior year locked, statements still balance. Manual journals post via the engine directly. |
| **2D** Tax engine | ✅ | `TaxCode` with effective-dated rate history, types (Standard/Zero/Exempt/OutOfScope/ReverseCharge/Withholding/Deemed), input/output accounts, recoverable %, return-box, jurisdiction; `ITaxCalculator` (on-net + tax-inclusive extraction, rate resolved at tax point); `IFiscalDeviceProvider` hook + `NullFiscalDeviceProvider`. Migration applied. Tax test green (effective-dated 14%→15%, exempt, inclusive) |
| **2E** FX revaluation (IAS 21) | ✅ | `IFxRevaluationService`: retranslates foreign monetary accounts at the closing rate, posts the unrealised gain/loss to FX gain/loss control accounts, auto-reverses next period; non-monetary items excluded. FX test green (100 EUR @1.10→1.20 = +10 unrealised gain, balance restored after reversal). Realised FX on settlement lands with AR/AP in Phase 4 |

**Phase 2 status: COMPLETE.** Accounting core (COA, posting engine, period control, year-end, tax, FX revaluation) + the first four financial reports, all enforced and tested (14 accounting tests). The trial balance always balances.
| **2F** Trial Balance, Nominal Activity, Balance Sheet, P&L | ✅ | `IGeneralLedgerReports` query services over posted journals: Trial Balance (balances), Income Statement (net profit), Balance Sheet (balances, with current-year earnings in equity), Nominal Activity (opening/movements/running/closing). 4 report tests green (11 accounting total) |

## Phase 3 — Reporting Engine + Designer
| Increment | Status | Notes |
|---|---|---|
| **3A** Report definition model | ✅ | `ReportDefinition` (page setup, parameters, data sources, groups, bands, elements with conditional formatting) + `.mrpt` JSON serializer |
| **3B** Expression engine | ✅ | Sandboxed `ReportExpressionEngine` (DynamicExpresso) with curated library (string/math/date/logical), field/param/page-variable access, `NumberToWords`/`AmountToWords`, `ToBase`. Reused by payroll later |
| **3C** Rendering engine + renderers | ✅ | Banded `ReportEngine` (multi-level grouping, scoped aggregates, expressions, conditional formats, suppression) → HTML, PDF (QuestPDF), CSV, plain-text/ESC-POS renderers. 16 unit tests green |
| **3D** Persist definitions + bind Phase 2 reports + bursting | ✅ | `rpt.report_definition` entity + migration; `SystemReports.TrialBalance()` definition; `ReportCatalogueSeeder` (idempotent); `IFinancialReportRenderer` renders the live Trial Balance to HTML/PDF/CSV/text through the engine (definition resolved user→company→system); `ReportBurster` splits a run by key. Verified with a rendered PDF. 6 more tests (72 unit + 16 accounting) |
| **3E** WPF viewer + designer | ⬜ | The banded engine, persistence, renderers and bursting are done; the interactive WPF drag-drop **designer** and in-app viewer remain as a focused follow-up |

**Phase 3 status:** the reporting **engine** (definition model, expression engine, banded rendering to HTML/PDF/CSV/ESC-POS, persistence, bursting, and the first bound report) is complete and tested — later modules can ship reports on it now. The visual WPF designer/viewer is the remaining piece.

## Decisions / deviations from brief
- **.NET 10** not .NET 8 (ADR-0001) — only SDK on the machine.
- **PostgreSQL 18** dev, min supported 16 (ADR-0002).
- **Product name One4All** not Meridian (ADR-0003).
- **No Docker** on the dev machine → integration tests fall back to the local PG18 instance; Testcontainers path kept for CI.

## Open questions for the product owner
- _(resolved)_ Local `postgres` password is `root`; stored in `src/One4All.Server/appsettings.Development.json` (gitignored). DB `one4all` created and migrated.
