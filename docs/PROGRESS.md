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

## Phase 4 — AR + AP + Cash Book
| Increment | Status | Notes |
|---|---|---|
| **4A/4B/4C** Trader master + sub-ledgers + allocation | ✅ | `Customer`/`Supplier` master; unified `OpenItem`/`Allocation` sub-ledger (`trd` schema, AR+AP). `ISalesLedgerService` (invoice, receipt) + `IPurchaseLedgerService` (invoice, payment) post through the central engine and create open items. `IAllocationService` matches debit↔credit items, reduces outstanding, and **realises FX** from differing base rates (posts to FX gain/loss + control). `ITradingReports`: aged debtors/creditors + open-item customer statement. Migration `AddTradingLedgers`. 3 trading tests green (settle + no FX, cross-rate realised gain 10, AP ageing) |
| **4D** Credit notes (AR + AP) | ✅ | `PostCreditNoteAsync` on both ledgers (reverse of an invoice), allocatable like any credit item. Test: sales credit note reduces the debtor to 95 |
| **4E** Payment run + bank reconciliation | ✅ | `IPaymentRunService` (selects due AP invoices, pays per supplier through the engine, allocates, generates a bank EFT file); `IBankReconciliationService` (import statement lines, auto-match to GL cash-book postings by amount, reconciliation statement with GL vs statement balance + unpresented items). `AddCashBookAndCreditNotes` migration (`cb.bank_account`/`cb.bank_statement_line`). 2 tests green (payment run pays 300 + EFT; bank rec auto-matches, difference 0) |
| **4F** Statement rendering | ✅ | `SystemReports.CustomerStatement()` + `IFinancialReportRenderer.RenderCustomerStatementAsync` renders a customer's open-item statement to HTML/PDF/CSV/text on the Phase 3 engine (burstable for bulk). Test: statement renders with balance 65. Dunning letters are a thin variant of the statement (follow-up). |

**Phase 4 status: COMPLETE.** AR/AP/Cash Book — trader master, invoice/receipt/payment/credit-note posting, open-item allocation with realised FX, ageing, **statements rendered to PDF**, **payment runs with EFT**, and **bank reconciliation**. 23 accounting tests. (Dunning letters + a full remaining-report seed are light follow-ups on the same engine.)

## Phase 5 — Inventory + Procurement + Workflow
| Increment | Status | Notes |
|---|---|---|
| **5A** Generic Workflow/approval engine (§7) | ✅ | `WorkflowDefinition`/`WorkflowStage` (ordered stages, quorum, value-band routing, eligible approvers), `WorkflowInstance` with pure advancement logic (approve/quorum/advance, reject, return, resubmit), `IWorkflowService` (start/act/status) with eligibility enforcement. Reusable by requisitions/POs/journals/payment-runs/budgets/master-data. `AddWorkflowEngine` migration. 7 unit + 1 integration test green |
| **5B** Inventory (items, warehouses, costing, movements) | ✅ | `Item`/`Warehouse` master; `StockBalance` with **moving weighted-average** cost; `StockMovement` history; `IInventoryService` (receive → Dr stock/Cr offset + recompute average; issue → Dr COGS/Cr stock at average; stock valuation). `AddInventory` migration. Test: receive 100@2 + 100@3 → avg 2.50, issue 50 → COGS 125, valuation 375 |
| **5C** Procurement (PO→GRN→3-way match) | ✅ | `PurchaseOrder`/`PurchaseOrderLine` (ordered/received/invoiced qty + status), `GoodsReceivedNote`/`GrnLine`; `IProcurementService`: create PO, receive goods (GRN accrues **GRNI** via the inventory receipt Dr stock/Cr GRNI + moving-average), and **3-way invoice match** (qty + price tolerance) clearing GRNI→AP and raising an AP open item. `AddProcurement` migration. Test: PO 100@2 → GRN accrues 200 → price mismatch rejected → matched invoice clears to AP 200 (creditors 200, stock 100@2=200). Requisition approval routes through the §7 workflow engine (5A). |

**Phase 5 status: COMPLETE.** Generic approval workflow, inventory with moving weighted-average costing, and procurement (PO→GRN→3-way match) — all integrated through the posting engine and tested. (Requisition UI, RFQ/tender, landed costs, batch/serial, budget-check-at-gates are follow-ups on this base.)

## Phase 6 — POS
| Increment | Status | Notes |
|---|---|---|
| **6A** POS transaction/accounting core | ✅ | `PosTerminal`, `TillSession` (float, X/Z read), `Sale`/`SaleLine`/`SaleTender`; `IPosService`: open till, **complete sale** (multi-currency tenders + change → Dr tenders/Cr revenue/Cr output VAT; then Dr COGS/Cr stock via the inventory issue), **X-read** (non-resetting) and **Z-read** (sequential, immutable, closes the session). `AddPointOfSale` migration. Test: sale 10@2 net 20+VAT 3, cash 25 with 2 change → gross 23, COGS via stock issue, X/Z reads, stock 100→90. Fixed a posting-engine bug (same account on multiple journal lines duplicated period balances). |
| **6B** Full-screen touch POS UI + offline sync | ⬜ | Large WPF UI follow-up (like the report designer); the transaction engine is complete |

**Phase 6 status:** POS transaction + accounting core complete and tested (till sessions, multi-currency tendering, COGS, X/Z reads). The full-screen touch UI + offline sync are the remaining UI work.

## Phase 7 — Vouchers + SOP
| Increment | Status | Notes |
|---|---|---|
| **7A** Voucher management (gift cards) | ✅ | `GiftCard` (serial, balance, expiry, status); `IVoucherService`: issue (Dr cash/Cr **liability**, not revenue), redeem (balance draw-down; the GL debit is the sale's voucher tender), balance enquiry, over-redeem guard, and **breakage** (expired residual → Dr liability/Cr breakage income). `AddGiftCards` migration. Test: issue 50 → redeem 20 → over-redeem rejected → breakage 30. |
| **7B** Sales Order Processing / Distribution | ✅ | `SalesOrder`/`SalesOrderLine` (ordered/despatched/invoiced qty + status); `ISalesOrderService`: create order, **despatch** (issues stock → cost of sales via the inventory service; back-order aware), **invoice** the despatched-but-uninvoiced qty via the AR service (Dr AR/Cr revenue/Cr VAT + open item). `AddSalesOrders` migration. Test: order 10@5 → despatch (COGS 10) → invoice 57.50 (net 50 + VAT 7.50); debtors 57.50; stock 100→90. |

**Phase 7 status: COMPLETE.** Gift-card liability accounting and the quote/order→despatch→invoice distribution flow, both integrated through the posting engine, inventory and AR.

## Phase 8 — Municipality
| Increment | Status | Notes |
|---|---|---|
| **8A** Property register, tariff engine, metered/valuation billing (§4.9) | ✅ | `Tariff`/`TariffBlock` flexible tariff engine (**flat / per-unit / stepped-block / rate-per-valuation**, minimum-charge floor, effective-dated, revenue account); `Property`/`PropertyServiceLink` (stand, ward, valuation, ratepayer customer, per-service tariff + meter); `Meter`/`MeterReading` (multiplier, rollover-guarded consumption). `IMunicipalBillingService`: record a reading (computes consumption), and **bill a service** — resolves the linked tariff, computes the charge from consumption/valuation, and raises it on the ratepayer's account **through the central AR ledger** (so it ages, reconciles and appears on the consumer statement like any receivable). `AddMunicipal` migration (`mun` schema). Tests: 6 unit (stepped 5→7.5, 25→25, 40→45; per-unit; rate-per-valuation; minimum-charge floor) + 1 accounting (stepped water: read 25 → consumption 25 → bill $25 → debtors $25, trial balance balances). |
| **8B** Housing, Land &amp; Estates (§4.10) | ✅ | `hse` schema. `LandParcel` (land bank, IAS 2 WIP), `HousingScheme`, `HousingStand` with a **tamper-evident SHA-256 chain of ownership** (`OwnershipRecord`, genesis→sale→cession, `VerifyOwnershipChain`), `HousingApplicant` (waiting list). `IHousingService`: **acquire** (Dr Land&Housing Stock WIP/Cr funding), **subdivide** (apportion cost equal/by-area → Dr Stands Available/Cr WIP), **sell on transfer of control** (raise price on beneficiary AR + release apportioned cost Dr COGS/Cr Stands Available), **transfer/cession** gated on rates-clear + dual approval (§4.10.6). `AddHousing` migration. Test: parcel 100k → 4 stands @25k → sell 1 for 40k (COGS 25k, debtors 40k) → cession blocked without clearance then allowed; ownership chain verifies; trial balance balances. |

**Phase 8 status: COMPLETE.** Municipal billing (tariff engine + property/meter register + consumption/valuation billing→AR) and Housing land-bank (IAS 2 WIP→stands→COGS + tamper-evident ownership chain + cession controls). 124 tests green (85 unit + 9 integration + 30 accounting). Deferred depth: waiting-list allocation/ballot run, instalment schedules, council rental/voids, servicing-cost apportionment from job costing, housing permissions wiring.

## Decisions / deviations from brief
- **.NET 10** not .NET 8 (ADR-0001) — only SDK on the machine.
- **PostgreSQL 18** dev, min supported 16 (ADR-0002).
- **Product name One4All** not Meridian (ADR-0003).
- **No Docker** on the dev machine → integration tests fall back to the local PG18 instance; Testcontainers path kept for CI.

## Open questions for the product owner
- _(resolved)_ Local `postgres` password is `root`; stored in `src/One4All.Server/appsettings.Development.json` (gitignored). DB `one4all` created and migrated.
