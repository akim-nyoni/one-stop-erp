# BUILD PROMPT — "Meridian ERP" (Sage 200–style Desktop ERP, C# / .NET / PostgreSQL)

> Paste this whole file into Claude Code as the project brief. Keep it at the repo root as `docs/BRIEF.md` and treat it as the contract for the build.

---

## 0. HOW YOU (CLAUDE CODE) MUST WORK ON THIS

You are acting simultaneously as:
- **Senior Software Architect** — you own the layering, module boundaries, contracts and non-functional requirements.
- **Senior C#/.NET Engineer** — you write production-grade, testable code. No pseudo-code, no `// TODO: implement later` in delivered increments.
- **Chartered Accountant (IFRS)** — you own correctness of double-entry, control accounts, period control, FX treatment (IAS 21), inventory valuation (IAS 2), revenue recognition (IFRS 15), and the statutory reports. **An ERP that produces a trial balance that does not balance is a failed build.**

### Working rules
1. **Do not build everything at once.** Follow the phased plan in §16. Finish a phase, make it compile, make its tests pass, demo it, then move on.
2. **Before each phase**, restate your plan (files you will create, tables, contracts) in a short checklist, then execute.
3. **Ask me** when a business rule is genuinely ambiguous. Do not silently invent accounting policy. Everything else — make the call and document it in `docs/decisions/ADR-xxx.md`.
4. **Every schema change is a migration.** Never edit an applied migration.
5. **Every posting path needs a test** that asserts Dr = Cr and that the sub-ledger reconciles to its control account.
6. Keep a running `docs/PROGRESS.md` with what's done, what's stubbed, what's next.
7. Write code in English, with XML doc comments on all public members of Core/Application layers.
8. **Never hardcode a business's behaviour.** Everything vertical-specific (supermarket vs municipality vs school) is configuration + plug-in modules, not `if (businessType == ...)` branches scattered through the code.

---

## 1. PRODUCT VISION

A **Windows desktop ERP** that a single codebase can deploy to:

| Vertical | Key characteristics it must handle |
|---|---|
| Supermarket | High-volume POS, thousands of SKUs, promotions, shelf labels, shrinkage, multi-till, cash-ups |
| Retail shops | Smaller catalogue, layaway/lay-bye, sales agents, credit customers |
| Bars / restaurants | Tables, tabs, split bills, recipes/portioning, happy-hour pricing, stock depletion per pour |
| Butchery / meat | Weighed items, price-per-kg, tare, carcass breakdown (yield/disassembly), cold-room batches, expiry |
| Sand / stones / concrete / bulk | Weighbridge tickets, gross/tare/net, m³ vs tonnes conversions, delivery scheduling, haulage charges, batching plant recipes |
| Construction | Job costing, BOQ, progress certificates, retention, subcontractors, plant hire, WIP |
| Schools | Students, classes/terms, fee structures, boarding, bursaries, sibling discounts, fee statements, arrears |
| Councils / municipalities | Properties (stands), valuation roll, meters, block tariffs, rates, refuse, sewer, licences, billing runs, arrears interest, debt management |
| Housing & land development | Land bank, subdivision into stands, waiting lists, allocation/ballot, instalment sales, cession & change of ownership, title deeds, council rental stock |
| Everything else | Generic trading, services, wholesale, distribution |

**Two modules cut across every vertical above and are not optional extras:** departmental **budgeting with permanent version history** (§4.18) and **payroll** (§4.15). A council, a school, a construction firm and a supermarket chain all budget by department and all pay people — and in all four the budget and the payroll are the numbers that get audited hardest.

**Currency reality:** multi-currency is a *first-class* requirement, not an add-on. Assume a market where a business trades daily in two or more hard/soft currencies simultaneously (e.g. USD + local currency), prices can be held in one currency and tendered in another, and rates move daily or intraday.

---

## 2. ARCHITECTURE — FOLLOW SAGE 200 STRUCTURE

### 2.1 Sage 200 concepts we deliberately mirror
- **Accounting System Manager (ASM)** equivalent: a central `SystemManager` module owning companies, users, periods, currencies, exchange rates, number sequences, VAT/tax codes, document layouts, and **deferred vs live posting**.
- **Ledgers as modules**: Nominal Ledger (GL), Sales Ledger (AR), Purchase Ledger (AP), Cash Book, Stock Control, Sales Order Processing (SOP), Purchase Order Processing (POP), Price Book, Bill of Materials, Project/Job Costing.
- **Nominal account structure**: `AccountCode + CostCentre + Department` composite (Sage 200's exact model). Reports must be runnable by any combination.
- **Trader accounts** (customer/supplier) with: account reference, terms, credit limit, currency, exchange-rate type, settlement discount, ageing basis, memos/contacts, transaction analysis codes, on-hold flag.
- **Open-item ledgers** with allocation/matching, part-allocation, payment on account, write-offs, contra (customer who is also a supplier).
- **Traceable items**: batch/lot & serial numbers with full traceability both directions.
- **Order → Despatch → Invoice** and **Requisition → Order → GRN → Invoice (3-way match)** lifecycles with authorisation routing.
- **Period-based accounting** with open/closed/future periods per module, and a formal year-end.
- **Never delete a posted transaction.** Reversals and credit notes only. Full audit trail.

### 2.2 Technical stack (use exactly this unless you raise an ADR)
- **.NET 8 LTS**, C# 12, nullable enabled, warnings-as-errors on Core/Application.
- **UI: WPF** + MVVM via `CommunityToolkit.Mvvm`, DI via `Microsoft.Extensions.Hosting`. Theming with a custom design system (touch-first for POS, dense-grid for back office). Use `Syncfusion`/`DevExpress`-free approach: build on `WPF` + open-source grid alternatives, or plain WPF `DataGrid` with virtualization. **Ship a POS that is usable on a 1024×768 touch screen with no mouse.**
  - *Architect's note:* structure the UI so a future **Avalonia** port is possible — keep zero business logic in code-behind; ViewModels must have no WPF references beyond `ICommand`.
- **Application server: ASP.NET Core 8 Web API** ("Meridian.Server"). The desktop client talks to the server; the server is the **only** thing that touches PostgreSQL.
  - Provide an **Embedded Mode**: for single-PC installs, the WPF app hosts the API in-process (`WebApplication` on localhost) so the same code path runs. No second code path, ever.
- **Data: PostgreSQL 16**, `Npgsql`. **EF Core 8** for the write model and master data; **Dapper** for reporting/high-volume reads and set-based posting queries.
- **Migrations:** EF Core migrations *plus* a raw-SQL migration runner for views, functions, triggers (`DbUp` or EF's `migrationBuilder.Sql`). Idempotent.
- **Auth:** local user store, Argon2id password hashing, JWT for client↔server, optional Windows AD sync later.
- **Background jobs:** `Hangfire` (Postgres storage) for billing runs, statement generation, revaluation, nightly rebuilds, email/SMS despatch.
- **Logging:** `Serilog` → rolling file + Postgres table + optional Seq.
- **Validation:** `FluentValidation`. **Mapping:** `Mapster`. **Mediator:** `MediatR` for command/query separation.
- **Testing:** `xUnit` + `FluentAssertions` + `Testcontainers.PostgreSql` for integration tests. `Bogus` for data seeding.
- **Reporting:** in-house report engine + designer (§13). PDF rendering via `QuestPDF` or `PdfSharpCore`; Excel via `ClosedXML`; barcode via `ZXing.Net`; charting via `OxyPlot` / `LiveCharts2`.
- **Packaging:** MSI/EXE via `WiX v4` or `Inno Setup`; auto-update channel; single-file publish for the licensing tool.

### 2.3 Solution layout

```
Meridian.sln
├─ src/
│  ├─ Meridian.Core/                 # Entities, value objects, domain services, enums. No I/O.
│  │   ├─ Accounting/                # Money, Currency, ExchangeRate, JournalEntry, Posting rules
│  │   ├─ Common/                    # Result<T>, DomainEvent, Entity, AuditableEntity
│  │   └─ Modules/<Module>/          # Aggregates per module
│  ├─ Meridian.Application/          # CQRS handlers, DTOs, validators, interfaces (ports)
│  ├─ Meridian.Infrastructure/       # EF Core, Dapper, repositories, Hangfire, file storage, email/SMS
│  │   ├─ Persistence/
│  │   ├─ Migrations/
│  │   └─ Devices/                   # Scales, ESC/POS printers, cash drawers, pole displays, fiscal devices
│  ├─ Meridian.Reporting/            # Report definition model, engine, renderers, exporters
│  ├─ Meridian.Reporting.Designer/   # WPF report designer (also embeddable in the main app)
│  ├─ Meridian.Server/               # ASP.NET Core Web API host
│  ├─ Meridian.Desktop/              # WPF shell + module UIs
│  │   ├─ Shell/                     # Navigation, ribbon/sidebar, company switch, period indicator
│  │   ├─ Modules/<Module>/          # Views + ViewModels per module
│  │   └─ Pos/                       # Full-screen POS (separate window, kiosk mode)
│  ├─ Meridian.Licensing.Core/       # Shared signing/validation primitives
│  ├─ Meridian.Licensing.Generator/  # SEPARATE vendor-only app (key issuer)
│  └─ Meridian.Updater/
├─ tests/
│  ├─ Meridian.UnitTests/
│  ├─ Meridian.IntegrationTests/     # Testcontainers Postgres
│  └─ Meridian.AccountingTests/      # The CA's test suite: balance, reconcile, revalue
├─ db/
│  ├─ sql/views/  sql/functions/  sql/triggers/  sql/seed/
└─ docs/
   ├─ BRIEF.md  decisions/  reports/CATALOGUE.md  PROGRESS.md  coa/
```

### 2.4 Cross-cutting non-negotiables
- **Multi-company, multi-branch/site, multi-warehouse** from day one. `CompanyId` on everything; row-level filtering enforced in the DbContext global query filter *and* re-checked in the API.
- **Optimistic concurrency** on every editable aggregate (`xmin` mapped as concurrency token).
- **Idempotency keys** on all posting endpoints (POS sync must never double-post).
- **Soft-delete only for master data**; transactions are never deleted.
- **Number sequences**: per company / branch / document type, with prefix, padding, year reset option, and **gapless mode** for fiscal compliance (allocate inside the posting transaction).
- **Everything money is `decimal`**, stored `numeric(19,4)` for amounts, `numeric(19,6)` for rates and unit prices, `numeric(19,4)` for quantities (weighed goods need ≥3 dp). **Never `float`/`double` anywhere near money.**
- **All timestamps `timestamptz`, stored UTC**, displayed in company time zone. Accounting *dates* are `date`.

---

## 3. THE ACCOUNTING CORE (CHARTERED ACCOUNTANT SECTION — GET THIS RIGHT FIRST)

Build this before any UI. Every other module is a client of it.

### 3.1 Chart of accounts
- Composite key: `AccountCode (8) + CostCentre (3) + Department (3)`, Sage 200 style.
- Account attributes: name, type (`Asset|Liability|Equity|Income|Expense`), report category (for financial-statement mapping), `IsControlAccount`, `ControlAccountType` (AR/AP/Stock/VAT Input/VAT Output/Bank/Retained Earnings/FX Gain/FX Loss/Suspense/WIP/Retention/…), allow-manual-journal flag, currency restriction, active, budget-enabled.
- **Control accounts are locked from manual journals.** Enforce in code AND with a DB trigger.
- Support a **reporting hierarchy** (tree) independent of the account code, so financial statements are configurable, plus **alternate hierarchies** (statutory, management, consolidation).
- Ship seed charts of accounts per vertical: `docs/coa/retail.csv`, `hospitality.csv`, `municipal.csv`, `housing.csv`, `school.csv`, `construction.csv` — each with its budget/vote structure and payroll expense analysis already mapped.

### 3.2 The posting engine (single, central, non-bypassable)
```csharp
public interface IPostingEngine
{
    Task<Result<PostingBatch>> PostAsync(JournalRequest request, CancellationToken ct);
}
```
Rules enforced inside one DB transaction:
1. Sum(Dr) == Sum(Cr) in **base currency**, and per-currency in transaction currency. Reject otherwise.
2. Posting date falls in an **open period** for the posting module; future-period posting only if enabled.
3. Every line: account exists, is active, is postable, permitted for the company/cost centre/department.
4. Every line carries: transaction currency amount, exchange rate, base amount, and (if enabled) second reporting-currency amount.
5. Allocates the next transaction number from the gapless sequence.
6. Writes the audit trail row (§10) referencing the source document.
7. Emits domain events for sub-ledger updates, and updates period balances (`gl_period_balance`) in the same transaction.
8. **Deferred posting option** (Sage 200 behaviour): transactions can be queued and posted in a controlled batch run, with a "pending postings" report and an approval step.

### 3.3 Multi-currency — full IAS 21 treatment
- **Base (functional) currency** per company; optional **second reporting currency** (dual-currency reporting) maintained on every posting.
- Currency master: code (ISO 4217), symbol, decimals, rounding rule, denomination list (for cash tenders), active.
- **Exchange rate tables** with rate *types*: `Spot`, `Daily`, `PeriodAverage`, `ClosingRate`, `Budget`, `Fixed/Contract`. Effective-dated with full history. Support **direct and indirect quotation**, and **triangulation** through base.
- Rate selection policy per: trader account, document type, module. A document can override the rate (permission-controlled, audited).
- **Transactional FX:** invoice at transaction-date rate; on settlement compute **realised** FX gain/loss to the configured accounts.
- **Period-end revaluation:** retranslate open monetary items (AR, AP, foreign bank accounts) at the closing rate → **unrealised** FX gain/loss journal, auto-reversing next period (configurable). Full revaluation report showing before/after and the difference per account/trader.
- **Non-monetary items** (stock, fixed assets, prepayments) are held at historic rate — the engine must not revalue them.
- **POS multi-currency tendering:** price list in currency A, customer pays partly in B and C, change given in a configured "change currency", with rounding to available denominations and a **rounding difference** account. Every tender line stores its own currency, rate and base value.
- **Cash book per currency**: a bank/cash account is denominated in one currency; its GL control shows base equivalent.
- Optional **IAS 29 hyperinflation restatement** module: CPI index table, restatement factors, monetary gain/loss, restated primary statements alongside historical cost. *(Build as an optional module — it's a real requirement in some markets.)*

### 3.4 Periods and year-end
- Fiscal year with N periods (12/13/4-4-5 configurable), each period per-module status: `Future | Open | Closed | Permanently Closed`.
- Soft close (block posting, allow adjustments with permission) then hard close.
- **Year-end routine:** validate TB balances, roll P&L to retained earnings, carry forward BS balances, open new year's periods, lock prior year, produce the year-end audit pack. Must be re-runnable safely and reversible before hard lock.

### 3.5 Tax engine
- Tax codes with: rate, effective-dated rate history, type (`Standard|Zero|Exempt|OutOfScope|ReverseCharge|Withholding|Deemed`), input/output GL accounts, box mapping for the tax return, recoverable %, applies-to (goods/services), jurisdiction.
- Item-level and account-level default tax codes; customer/supplier tax status overrides (exempt certificates, with expiry and warning).
- **Withholding tax** on supplier payments (rate by supplier tax-clearance status), with WHT certificate printing and a WHT remittance return.
- Tax point rules (invoice date vs delivery vs payment/cash accounting).
- **Fiscalisation hook:** an `IFiscalDeviceProvider` abstraction so a country-specific fiscal device / e-invoicing API (e.g. a revenue authority's fiscal data management system) can be plugged in — fiscal day open/close, receipt signing, QR/verification code on the receipt, Z-report submission, offline queue with retry. Ship a `NullFiscalProvider` and one reference implementation skeleton.

### 3.6 Inventory valuation (IAS 2)
- Costing methods per item **or** per item group: `FIFO`, `WeightedAverage` (moving), `Standard` (with purchase price variance + usage variance to separate accounts), `SpecificIdentification` (serial/batch).
- Cost layers table for FIFO with full consumption history so COGS is auditable line by line.
- **Landed costs**: freight, duty, clearing, insurance apportioned by value/weight/volume/quantity onto GRN cost.
- Negative stock: prevent by default, allow by exception (permission + reason + audit), with cost correction when the receipt lands.
- Stock provisions: slow-moving/obsolete/NRV write-down with journal and report.
- **Weighed / catch-weight items:** an item can have a *nominal* unit and an *actual weight*; stock is tracked by weight, sold by weight, priced per kg/tonne/m³. Yield tracking for disassembly (carcass → cuts) with cost apportionment by relative sales value.

### 3.7 Revenue recognition (IFRS 15)
- Point-in-time for POS/retail/trade.
- **Over-time for construction/jobs**: input method (cost-to-cost) — compute % complete, recognise revenue and cost, post **WIP / accrued income / deferred income** and **retention** correctly. Contract asset vs contract liability presentation.
- Deferred income schedules for schools (fees billed in advance, recognised over the term) and municipalities (annual licences).

---

## 4. MODULE SPECIFICATIONS

For each module: master data, transactions, workflows, permissions, reports, and integration points.

### 4.1 Point of Sale (POS) — flagship module

**Shell**
- Separate **full-screen / kiosk** window, keyboard-first with large touch targets. Configurable layout per terminal profile (supermarket / bar / butchery / weighbridge kiosk).
- Login by PIN / card swipe / fingerprint hook; fast cashier switch without closing the sale.
- Panels: transaction grid (left), product browse (centre), function keys + **numberpad** (right), totals bar (bottom), customer/currency bar (top).

**Product finding (assume NO barcode scanner is available, but fully support one if present)**
- **Live incremental search** as you type: matches on code, name, short name, alias/synonym, PLU, supplier code, and description — debounced, indexed (`pg_trgm` GIN indexes + `unaccent`), returns in <100 ms on 100k SKUs.
- **Category / department / sub-category tiles**, multi-level drill-down, back button, breadcrumb.
- **Favourites / hot-key grid** per terminal and per cashier: colour-coded, drag-to-arrange, pages of tiles.
- Filters: department, category, brand, supplier, price band, in-stock only, promo only, recently sold.
- **PLU codes** (short numeric) typed on the numberpad — classic supermarket/greengrocer flow.
- **Scanner optional**: HID keyboard-wedge capture, plus **embedded price/weight barcodes** (EAN-13 with price or weight in the payload) for butchery/deli — configurable parsing template.
- **Scale integration**: read live weight from a connected scale (RS-232/USB/TCP), tare, hold, manual weight entry with permission + audit.

**Numberpad behaviour**
- Modes: `Quantity` / `Weight` / `Price override` / `Discount %` / `Discount amount` / `Tender amount` / `PLU`.
- `Qty × Item` pattern (type 3, press ×, tap item), `+/-`, decimal for weights, clear/backspace, big Enter.

**Sale operations**
- Line: change qty/weight, price override (permission + reason), line discount (% or value), line note, line void, gift-wrap/serial/batch prompt, kitchen/prep note.
- Sale: discount on total (% or value, or promotion-driven), **recall/park/hold** any number of suspended sales with a name/table/customer tag, **resume**, **void sale** (reason + supervisor approval), **no-sale** (drawer open, audited).
- **Refunds/returns**: with-receipt (pull original, select lines, enforce quantity limits and time window) and without-receipt (permission, reason, restocking rules). Returns must restore stock at the **original cost**, not the current cost. Exchange (return + sale in one transaction, net tender).
- **Split bill**: by seat/guest, by items (drag lines to bills), by equal N-way, by percentage, by amount. Each split bill can tender differently. Partial payments and re-splitting until closed.
- **Table / tab management** (bars & restaurants): floor plan with tables, open tabs by name or customer, transfer lines between tabs, merge tabs, transfer table to another waiter, tab time-open warnings, course/round handling, kitchen/bar ticket printing per prep station.
- **Age-restricted items** prompt; **max-quantity** limits; **prescription/controlled** flags.

**Pricing & promotions**
- Price Book: multiple price lists (retail, wholesale, staff, contract, per-customer, per-price-band), effective dating, currency per list, quantity break pricing, cost-plus or margin-based pricing.
- **Promotions engine**: mix & match, buy X get Y, N for price, % off category, meal deals, happy hour (time windows), member-only, coupon codes, lowest-price-free, compounding rules and priority, per-store scheduling. Every promotion line records the discount reason and posts to a discount GL account so margin is analysable.
- **Loyalty**: points earn/redeem, tiers, member pricing, points expiry, statements.

**Tendering**
- Tender types: cash (multi-currency, denomination entry), card (with terminal integration hook), mobile money / wallet, bank transfer, cheque, voucher/gift card, credit (on-account → posts to AR), staff account, loyalty points, split across any combination.
- Live **change due** computation across currencies, with configured change currency and denomination rounding.
- **Rounding rules** (e.g. nearest 0.05) with a rounding GL account.
- Tips (bars): tip by tender, tip pooling report.

**Cash management**
- Till/drawer sessions: float in, **X-read** (mid-shift, non-resetting), **Z-read** (end of shift, resetting, sequential and immutable), pickups/drops to safe, petty cash paid-outs (with GL coding and reason), **blind cash-up** option, variance by currency and denomination, supervisor sign-off.
- Safe/treasury: consolidate tills, bank deposits, deposit slip printing, bank reconciliation link.

**Offline resilience (mandatory)**
- Terminal keeps a local encrypted store (SQLite) of the product catalogue, prices, promotions, customers, open tabs.
- If the server is unreachable, POS keeps selling; transactions queue with idempotency keys and sync on reconnect with conflict rules (stock goes negative-allowed in offline, reconciled on sync).
- Clear on-screen state indicator: Online / Offline / Syncing (n pending).

**Hardware**
- ESC/POS receipt printers (USB/serial/network), cash drawer kick, pole/customer display, scales, fiscal printers, card terminals, label printers (shelf-edge labels and scale labels). All behind `IDeviceProvider` interfaces with a simulator implementation for dev/test.

**POS accounting**
Each closed sale posts: Dr Cash/Bank/AR/Voucher-liability per tender line, Cr Revenue by department, Cr Output VAT, Dr COGS / Cr Stock, Dr Discount-given. Rounding and FX differences to their own accounts. Every posting traceable back to the receipt number.

### 4.2 Inventory / Stock Control
- Item master: code, description, short description (receipt), long description, barcodes (multiple), PLU, category tree, brand, supplier(s) with lead time and last cost, tax code, item type (`Stock|NonStock|Service|Kit|BOM|Weighed|Batch|Serial|Fuel|Deposit|Voucher`), UoM set with conversions (each/box/pallet, kg/tonne, litre, m³), weight/volume/dimensions, shelf life, min/max/reorder level & quantity, ABC class, images, attributes/variants (size/colour/flavour matrix), country of origin, HS code.
- **Multi-warehouse / multi-bin**, with locations, bin capacity, pick sequence, and per-warehouse reorder levels and costs.
- Transactions: goods receipt, issue, transfer (with in-transit), adjustment (with reason codes), write-off, stock take/cycle count (count sheets, variance approval, blind counts, recounts), assembly/disassembly, **kits/BOM** (phantom and manufactured), rework.
- **Batch/lot & serial** tracking: capture on receipt, enforce on issue, FEFO picking, expiry alerts, recall report (trace forward from batch to customers, backward from sale to supplier).
- **Weighbridge module** (bulk/sand/concrete): vehicle register, first weigh (gross)/second weigh (tare), net computation, manual weigh with permission, weighbridge ticket printing, ticket → delivery note → invoice, moisture/deduction adjustments, haulage/transport charging by distance zone, m³↔tonne conversion factors per product, multi-load orders and daily tonnage summaries.
- **Concrete batching**: mix designs (recipes) consuming cement/sand/stone/water/admixture per m³, batch tickets, slump/strength test records.
- Stock valuation snapshot at any historical date (essential for audit).
- Replenishment: suggested order report by reorder point, min/max, or forecast (simple moving average / seasonal factor).
- Consignment stock, supplier-owned stock, customer-owned stock flags.

### 4.3 Procurement (Requisition → PO → GRN → Invoice) with Sage 200-style authorisation
- **Requisition**: raised by any user, header + lines (item or free-text/service), suggested supplier, budget code (cost centre/department/job), required-by date, attachments, justification.
- **Multi-level authorisation workflow engine** (this must be generic and reusable by every module):
  - Rules by: value band, currency, department, cost centre, item category, project, requester.
  - Sequential and/or parallel approvers, quorum ("any 2 of 3"), escalation on timeout, delegation/out-of-office, substitute approvers.
  - **Budget check** at submission and at approval: available = budget − actual − committed (POs) − requisitions in flight. Block or warn per policy.
  - Actions: approve, reject (reason mandatory), return for amendment, add approver ad-hoc, comment thread.
  - Full history: who, when, from which machine, what the document looked like at that moment (snapshot hash).
  - Notification centre in-app + email.
- **RFQ / tender**: send requirements to N suppliers, capture quotes, comparison matrix (price, lead time, terms, score), award → PO, and an audit-ready adjudication report.
- **Purchase Order**: from approved requisition or direct; multi-currency; delivery schedule lines; part-delivery; amendments with version history and re-approval rules; PO print/email; commitment accounting (encumbrance) posted to a commitment ledger.
- **Goods Received Note**: receipt against PO (full/partial/over-receipt tolerance), quality inspection hold, reject/return-to-supplier, batch/serial capture, landed cost entry, GRN → accrual posting (Dr Stock / Cr GRNI).
- **Invoice matching (3-way)**: PO ↔ GRN ↔ supplier invoice, with tolerance rules on price and quantity; mismatches routed to a query/dispute queue; matched invoices post Dr GRNI / Cr AP.
- Supplier returns, debit notes, rebates, settlement discount.
- **Supplier master**: categories, tax clearance status & expiry, bank details (with change-approval workflow — a classic fraud vector, so make bank-detail changes a dual-authorisation event), contracts, performance scorecard (on-time %, quality %, price variance), blacklist.

### 4.4 General Ledger / Nominal Ledger
- Manual journals: single, recurring (schedules with auto-generate), reversing, accrual/prepayment templates, template journals, import from CSV/Excel, multi-currency journals, inter-company journals (with due-to/due-from).
- Journal approval workflow (maker-checker) above a threshold.
- Budgets: **see §4.18 — budgeting is a full module, not a GL feature.** The GL's only obligations are to expose actuals by the full dimension set for comparison and to refuse to post against a vote with no appropriation when fund control is enabled.
- Allocations/apportionments: overhead recharge rules (fixed %, headcount, floor area, usage driver) run as a period-end routine.
- Consolidation: multi-company consolidation with intercompany elimination, currency translation of subsidiaries (IAS 21 — closing rate for BS, average for P&L, CTA to equity reserve).
- Nominal transaction enquiry with unlimited drill-down: TB → account → transaction → source document → originating POS receipt/PO/invoice.

### 4.5 Accounts Receivable (Sales Ledger)
- Customer master: account ref, type (`Retail|Trade|Student|PropertyOwner|Tenant|Debtor`), currency, credit limit & credit-limit check policy, payment terms, settlement discount, ageing basis (by invoice date or due date), statement type (open item / balance forward), delivery addresses, contacts, price list, sales agent, tax status, on-hold, dunning group.
- Documents: invoice, credit note, debit note, receipt, payment on account, refund, journal, write-off, interest charge, contra with AP.
- **Allocation/matching** screen: auto-match by reference/amount, manual, part allocation, unallocate (permission + audit), settlement discount handling, FX realised gain/loss on allocation.
- **Credit control workbench**: aged debt with drill-down, promise-to-pay diary, dunning levels with configurable letters/SMS/email, hold/release, credit limit approval workflow, bad-debt provision (expected credit loss matrix under IFRS 9 — provision % by ageing bucket) and write-off with VAT relief.
- **Statements** (see §14 — critical requirement): open-item and balance-forward, per currency and in base, with ageing summary, printable and emailable in bulk, with remittance slip.
- Direct debit / recurring billing schedules; deposits and advance payments; instalment/lay-bye plans with schedules and forfeiture rules.
- Sales agents/reps: assignment, commission rules (by margin, value, collection-based), commission statements and accruals.

### 4.6 Accounts Payable (Purchase Ledger)
- Supplier invoices (matched and non-PO), credit notes, expense claims, recurring invoices, prepayments/advances to suppliers.
- **Payment run**: select by due date/supplier/currency/priority, proposed payment list, approval, part-payment, payment method (EFT file generation in bank-specific formats, cheque printing with numbering, cash, mobile money), remittance advice generation and emailing, and the resulting GL posting including WHT deduction and FX.
- Supplier statement reconciliation tool: import/key the supplier's statement, auto-match against our ledger, produce a **reconciliation report** listing items on their statement not on ours and vice versa.
- Aged creditors, cash requirement forecast, unmatched GRNI report (a classic audit finding — make it first-class).

### 4.7 Cash Book / Treasury
- Bank accounts per currency, cash accounts, petty cash (imprest system with top-ups and reconciliation), mobile-money wallets.
- Receipts and payments, transfers between accounts (including cross-currency with a realised FX difference), standing orders.
- **Bank reconciliation**: manual and auto-match, statement import (CSV/OFX/MT940/CAMT.053), rules engine for auto-coding recurring items, unpresented items, reconciliation statement report, and locking of reconciled periods.
- Cash flow forecast: from AR due dates, AP due dates, recurring items, payroll, and manual forecast lines.

### 4.8 Voucher Management
A deliberately broad module — covers several distinct voucher concepts; implement all:
1. **Gift cards / gift vouchers**: issue (sold at POS → **liability**, not revenue), serial/barcode ranges, activation, balance enquiry, partial redemption, top-up, expiry with breakage recognition, reissue/replace, lost/stolen block, full transaction history, liability report by age.
2. **Discount/promo vouchers & coupons**: single-use or multi-use codes, campaigns, validity windows, eligibility rules (customer group, item category, minimum spend), redemption caps, redemption analysis.
3. **Payment vouchers (accounting)**: the classic Payment Voucher / Receipt Voucher / Journal Voucher / Petty Cash Voucher documents with their own numbering, approval workflow, supporting-document attachment, and printed voucher layout with signature blocks (prepared by / checked by / approved by / received by). **Municipal and school clients will insist on this.**
4. **Requisition & store issue vouchers**: internal stock requisition vouchers with approval and issue notes.
5. **Fuel/meal/allowance vouchers**: issued to staff or contractors, controlled by serial number, reconciled against supplier invoices.

### 4.9 Municipality / Council Billing
- **Property register**: stand/erf number, deeds reference, address, ward, suburb/zone, GPS coordinates, size, land use/zoning (residential/commercial/industrial/agricultural/institutional), **valuation roll** with valuation history and effective dates, supplementary valuations, objections register.
- **Owner & tenant register** with ownership history, transfers (with clearance certificate issuance and pro-rata settlement), joint ownership shares, agents/managing agents, correspondence address separate from property address.
- **Services linked to a property**: water, sewer/sanitation, refuse/waste, rates (property tax), street lighting, roads levy, fire levy, ambulance, cemetery, licences, rentals, parking, and custom service types. Each service link has: start date, end date, tariff category, meter (if metered), deposit held, and status.
- **Tariff engine** (must be genuinely flexible):
  - Flat rate, per-unit, **stepped/block tariffs** (e.g. 0–10 m³ @ x, 11–30 @ y, 31+ @ z), rate-per-valuation (cents in the dollar), area-based, per-bin/per-collection, minimum charge, fixed availability charge + consumption charge, seasonal rates, prepaid.
  - Tariffs vary by consumer category (domestic / commercial / industrial / institutional / indigent) and by ward/zone.
  - **Effective-dated tariff versions** so historical bills can be recomputed and explained.
- **Meters**: meter register (serial, type, size, digits, multiplier, install/remove dates, location), meter replacement with reading carry-over, **readings** (manual, route-based, mobile import CSV/API, estimated with estimation rules, actual), reading validation (high/low consumption, rollover, negative, tamper flags), reading routes and meter readers, consumption history graphs.
- **Billing run**: select ward/route/cycle/service, period, generate bills in a reviewable **proposed** state, exception report, approve, post to AR, print/email/SMS. Must be re-runnable, reversible before posting, and fully auditable. Support monthly, quarterly, and annual cycles; pro-rata for mid-period connections/disconnections.
- **Interest/penalty on arrears**: configurable rate, compounding rule, grace period, exemption flags; posted as a separate transaction type.
- **Debt management**: arrears ageing, payment plans/arrangements with instalment schedules and default tracking, disconnection/reconnection workflow with orders and fees, legal handover, summons register, write-off approval workflow, **indigent/social-tariff register** with means-test review dates and subsidy postings.
- **Prepaid** integration hooks (vending token purchase, credit-off-arrears % rule).
- **Consumer/citizen enquiry desk**: single screen showing property, services, meters, readings, bills, payments, arrears, arrangements, complaints/tickets, and correspondence — with instant statement print.
- **Clearance certificates / rates clearance** for property transfer with pro-rata computation.
- Licensing & permits sub-module: business licences, liquor licences, shop licences, building plan approvals, hawker permits — with renewal cycles, inspection records, fees and penalties.
- Cemetery, market stalls, hall hire, and other council revenue streams as configurable "service types" reusing the same billing engine.

### 4.10 Housing, Land & Estates Management

> **Write this module as a Housing Officer would specify it, not as a developer would guess it.** The department's daily reality is: a finite land bank, a waiting list far longer than the stands available, applicants who will try to be on the list three times under three spellings of their name, agreements signed years ago whose instalments were never chased, stands sold twice by someone who shouldn't have had the access, transfers that must not proceed until rates are cleared, and a filing room full of paper agreements whose chain of ownership is the only proof a family owns their home. **The module's primary job is to make the chain of ownership of every stand complete, provable and tamper-evident from acquisition to title deed.** Everything else is secondary to that.

This module works hand-in-glove with §4.9 (Municipality) — Housing *creates* the property record that Municipality then bills — but it must be separately licensable, because private developers, housing cooperatives and estate agencies will buy it without the rates module.

#### 4.10.1 Land bank & acquisition
- **Parcel register** (the parent land, before subdivision): parcel reference, description, locality/ward, GPS polygon or boundary coordinates, area (ha and m²), current land use/zoning, tenure (freehold / state land / leasehold / communal / customary / trust), existing improvements, current occupation status (vacant / occupied / informally settled / under dispute), and photographs.
- **Acquisition record** per parcel, with method: **purchase, donation/bequest, state land grant or allocation, compulsory acquisition/expropriation, land swap/exchange, joint venture contribution, escheat/reversion, regularisation of existing occupation**. Capture: seller/grantor, negotiation history, offer and counter-offer, approved purchase price, valuation obtained (valuer, date, value), council resolution/minute reference authorising the acquisition, acquisition date, transfer date, deed/title reference acquired, and all acquisition costs.
- **Due diligence checklist** with mandatory sign-off before acquisition can be marked complete: title search result, encumbrances and bonds, caveats, servitudes and way-leaves, boundary disputes, outstanding rates on the parcel, environmental impact assessment status, heritage/archaeological clearance, flood-plain or geotechnical constraints, wetland status, existing occupants/compensation liability.
- **Acquisition cost build-up**: purchase price + survey + legal/conveyancing + valuation + compensation to occupants + stamp duty/transfer fees + interest on acquisition loans (if capitalisable) → **total parcel cost**.
- Compulsory acquisition sub-process: notice served, objections register, compensation assessment, compensation offers and payments, disputes/arbitration, possession date.
- Land disposal in the other direction: sale of surplus council land, land exchange, lease of land to third parties — same controls, reverse direction.

#### 4.10.2 Planning, survey, subdivision & servicing
- **Layout plan register**: layout number, planner, date submitted, approval authority, approval date, approved density (low/medium/high), number of stands by use (residential / commercial / industrial / institutional / open space / roads / schools / clinics), amendments and re-approvals with version history.
- **Subdivision engine**: split a parent parcel into child **stands/erven** (and support consolidation of stands back into one, and re-subdivision of a stand). Each child stand inherits lineage — you must be able to walk from any stand back to the parcel it came from and the acquisition that created it.
- **Stand register** — the core master record: stand/erf number, scheme/project, ward/suburb/zone, street address, size (m²), intended use and zoning, density category, survey status, **surveyor-general diagram number**, pegging date and surveyor, coordinates, service availability flags (water / sewer / road / electricity / storm water), servicing phase, status (see state machine below), selling price, valuation, reserved/blocked flags, photographs, and a free-form remarks history.
- **Stand status state machine** (enforce transitions in code; no jumping states, every transition audited with user, date and authority):
  `Raw land → Planned → Surveyed → Under servicing → Serviced → Available → Reserved → Offered → Allocated → Under agreement → Fully paid → Title applied → Title registered → Title handed over`
  with side branches: `Withheld / Blocked (dispute or caveat)`, `Repossessed`, `Surrendered`, `Cancelled offer`, `Consolidated away`, `Re-subdivided`.
- **Servicing projects**: link a servicing contract (from §4.12 Job Costing) to a scheme so actual servicing cost flows in — roads, water reticulation, sewer, storm water, electrification. Then **apportion servicing cost to stands** by area, by frontage, by equal share, or by zoning weighting, producing an auditable **cost per stand**.
- **Selling price build-up** per stand: land cost + servicing cost + overhead/administration recovery + development levy + margin/cost-recovery factor = price, with the ability to set price by scheme, by stand size band, or per stand, with effective dating and approval workflow. Price changes are versioned — a beneficiary's price is frozen at offer date.
- **Development/infrastructure levies**: endowment fee, intrinsic land value charge, infrastructure development levy, connection fees — configurable charge types applied at allocation or at transfer.

#### 4.10.3 Waiting list & applicant management
- **Application intake**: applicant + spouse/co-applicant, national ID (validated format, checked for duplicates), date of birth, gender, disability status, marital status and regime, dependants, physical and postal address, contacts, employer, income band, current tenure (lodger / tenant / homeless / staying with relatives), period of residence in the local authority area, and supporting documents (ID copy, proof of residence, payslip, affidavit) attached and flagged verified/unverified with the verifying officer's name.
- **Qualification rules engine** (configurable, never hardcoded): minimum age, minimum period of residency, maximum income for a category, **must not already own property in the area** (auto-checked against the stand register, ownership history and the municipal property register by national ID), not previously allocated and forfeited, tax/rates clearance, first-time-buyer status.
- **Registration**: unique waiting-list number and registration date (the date that determines seniority — protect it fiercely; any change to a registration date is a dual-authorisation, fully audited event). Registration fee receipting. Waiting list **category** (low/medium/high density, flats, rental, serviced/unserviced) and **quota groups** (e.g. civil servants, war veterans, persons with disabilities, youth, women, people on the list longest, cooperative members) with configurable quota percentages per scheme.
- **Priority scoring**: points model — years on list, residency period, dependants, disability, age, current housing conditions, cooperative membership — weights configurable per council, with the score recomputed on a schedule and a full explanation of how each applicant's score was derived (an applicant *will* ask, and the officer must be able to print it).
- **Periodic confirmation/renewal**: annual or biennial re-registration; non-responders flagged, then suspended, then removed after a grace period — all with notification history proving they were contacted.
- **Duplicate and fraud detection**: fuzzy matching on name + ID + date of birth + phone across the waiting list, past allocations, current ownership and the deceased register; a dedicated **duplicate applicants queue** for an officer to merge or reject with reasons.
- Applicant self-service: enquiry by waiting-list number or ID showing position, category, score and last update — exposed through the API for a web/USSD front-end later.

#### 4.10.4 Allocation (the highest-fraud-risk process in the whole ERP — control it accordingly)
- **Allocation run**: select a scheme and stand set, select waiting-list category and quota rules, and generate a **proposed allocation list** by seniority, by score, or by **ballot/draw** (with a seeded, reproducible, logged random draw — the seed and the input list hash are stored so the draw can be independently re-verified).
- **Allocation committee**: the proposed list is a document that goes through the §7 approval workflow — committee members, meeting date, minute reference, approvals, rejections with reasons, substitutions (a substitution must state why the skipped applicant was skipped).
- **Hard controls, enforced in the database, not just the UI:**
  - A stand may have **exactly one active allocation**. Unique partial index. A second allocation attempt is blocked and raises an exception report entry.
  - **One beneficiary, one stand** per scheme/council (configurable to N) checked on national ID, including against ownership history and the spouse's ID.
  - Allocation requires **dual authorisation** above the officer level; the allocating officer cannot be an approver.
  - Any change to an existing allocation (swap of stand, change of beneficiary, cancellation) is a separate approved document, never an in-place edit.
- **Offer letter**: generated with price, charges, deposit required, payment terms, acceptance deadline, and the stand's exact description. Offers have a life — expired offers auto-lapse to an exceptions queue for the officer to lapse or extend (with reason).
- **Acceptance**: deposit receipted (feeds AR/Cash Book), acceptance signed, allocation confirmed, stand status moves on. **Non-acceptance/decline/forfeiture**: reason, forfeiture of deposit per policy, stand returns to available pool, applicant returns to or is removed from the list per rules.
- **Appeals register**: unsuccessful applicants' appeals, grounds, hearing, outcome, and any resulting re-allocation.
- Every allocation carries an immutable **allocation certificate number** and a printable allocation letter.

#### 4.10.5 Tenure, agreements & instalment sales
Support every tenure type a housing department actually issues:
- **Outright cash sale** — pay in full, proceed to transfer.
- **Sale on instalments / rent-to-buy / lease-with-option-to-purchase** — deposit, term (months/years), interest rate (flat, reducing balance, or zero), instalment amount, payment day, escalation, penalty interest on arrears, early settlement with rebate calculation.
- **Council rental** (housing stock) — see 4.10.7.
- **Lease of stands** (commercial/industrial/agricultural), including **long leases (e.g. 99-year)** with rent reviews.
- **Lodger permits**, **site-and-service schemes**, **cooperative allocations** (allocation to a cooperative, which sub-allocates to members — track both levels).
- **Agreement register**: agreement number, type, parties, stand, price, charges, terms, signature dates, witnesses, special conditions, and the generated agreement document itself stored against the record. Amendments create a new version; the superseded version is retained.
- **Instalment schedule** generated on signature: full amortisation table (capital, interest, balance) printable for the beneficiary. Receipts allocate against the schedule (oldest-first or specified-instalment, per policy), producing an always-current arrears position.
- **Development covenant**: "build within N years or the stand reverts". Track building plan submission and approval (link to §4.9 licences/permits), construction commencement and completion inspections, extensions granted (with fee), breaches, penalty levies, **notice of intention to repossess**, and the repossession workflow with committee approval, refund-less-deductions computation, and return of the stand to the available pool.
- **Default & cancellation**: arrears stages → reminder → demand letter → notice of breach → notice of cancellation → cancellation approved → refund computation (amounts paid less occupation rental, less administration charge, less arrears) → stand reverts. Every stage a dated document with proof of service.

#### 4.10.6 Change of ownership, cession & title (specified explicitly, because this is where councils get sued)
- **Transfer types**, each with its own required-document checklist and approval route:
  `Sale/assignment to a third party · Cession of rights (pre-title, the common case) · Inheritance / deceased estate · Donation or gift · Divorce or court order · Matrimonial addition or removal of spouse · Transfer to or from a company/trust · Change of name (marriage, deed poll) · Correction of a captured error · Surrender to council · Repossession by council · Substitution of a deceased joint holder`
- **Mandatory pre-conditions before a transfer can be submitted** (configurable, but these are the defaults, and each one is a checklist item with an attachment and a verifying officer):
  1. **Municipal clearance** — rates, water, refuse and all service accounts on the property paid in full or a cleared arrangement in place. Pull this live from §4.9; do not let an officer type "cleared".
  2. **Housing account clear** — instalments up to date, or the balance settled, or formally assumed by the transferee with approval.
  3. Identity verification of transferor and transferee (ID, biometrics/photo, affidavit).
  4. **Spousal consent** where the marital regime requires it, or a court order.
  5. Original agreement / title produced or a sworn loss affidavit with police report.
  6. For deceased estates: death certificate, letters of administration / executor's appointment, distribution account, beneficiary identification.
  7. Transfer/cession fee, administration fee and any endowment or transfer duty paid — receipted before submission.
  8. Where applicable, capital gains or transfer-duty withholding computed and remitted (link to §3.5 tax engine).
- **Workflow**: application lodged → checklist verified by Housing Officer → valuation/consideration recorded → **dual approval** (Housing Officer + Director/Committee, value-banded via §7) → cession/transfer agreement generated and signed → registers updated atomically → new agreement or title issued → old holder's account closed and new holder's account opened in AR with any agreed balance carried over.
- **Ownership history chain**: every stand carries an ordered, append-only list of holders — from, to, transfer type, effective date, consideration, approving authority, document references. **Never overwrite the current owner; always append and close the previous period.** The chain is part of the audit hash-chain (§6) so a tampered ownership record is detectable.
- **Joint ownership**: multiple holders with shares (%), each with their own ID and consent requirements; transfer of one share only; survivorship rules.
- **Caveats, disputes & blocks**: a stand or an account can be flagged `Blocked` with a reason (court interdict, family dispute, suspected fraud, double allocation under investigation, boundary dispute). A blocked stand cannot be allocated, transferred, or have title processed. Only a senior role can place or lift a block, and both actions are audited with a mandatory reason and supporting document.
- **Title deed pipeline**: application to the deeds registry, conveyancer assigned, documents lodged, queries raised and resolved, deed number and registration date captured, deed received into council custody, **handover to the owner with signed receipt**, and deeds held but uncollected tracked and chased. Report on pipeline age at every stage — "deeds sitting with the conveyancer for 400 days" is exactly the number a housing director is asked for in a council meeting.
- **Sectional title / flats**: building → units → common property, unit participation quotas, body corporate levies, and transfer of a unit rather than a stand.

#### 4.10.7 Council rental housing stock
- **Unit register**: property, unit number, type (house / flat / room / hostel bed / cottage), rooms, amenities, floor area, condition grade, last inspection, rent category.
- **Rent tariffs** by unit type, suburb and income category, with effective-dated escalation and a rent review run.
- **Tenancy**: tenant (from the waiting list rental category), lease agreement, start/end, deposit held (a liability, not income), occupants list, subletting/lodger rules and permits, rent debit run (monthly, reusing the §4.9 billing engine), receipting, arrears, and the **eviction workflow** (reminder → demand → notice to vacate → legal handover → court order → eviction → unit returned, inspected and re-let).
- **Voids management**: vacant units, days void, reason, refurbishment work order (to §4.12 job costing), re-let date, rent lost to voids.
- **Maintenance**: tenant-reported faults raised as service tickets (§4.16), repairs recharged to tenant where the damage is tenant-caused, planned maintenance programme, and cost per unit history.
- **Inspections**: scheduled and ad hoc, with findings, photographs, and follow-up actions.

#### 4.10.8 Schemes, projects, partners & informal settlements
- **Housing scheme/project** as the organising unit: scheme name, type (council-delivered, PPP/joint venture, cooperative, employer-assisted, rural/peri-urban), target beneficiaries, stand counts by category, budget, funding source (own revenue, loan, grant, developer contribution), start and completion dates, and delivery milestones.
- **Developer/partner agreements**: developer builds and/or services; council contributes land; split of stands between council quota and developer quota; delivery obligations, penalties, and a reconciliation of stands handed over vs contracted.
- **Housing cooperatives**: cooperative register, members, contributions, allocation to the cooperative and sub-allocation to members, and the cooperative's own account statement.
- **Informal settlements / regularisation**: settlement register, enumeration exercises (household survey data), structures and occupants, eligibility for regularisation, upgrading plan, conversion of an enumerated household into a waiting-list registration or a direct allocation on regularisation — with the same anti-double-allocation checks.
- **Beneficiary subsidy/grant tracking** where a national or donor housing subsidy part-funds a beneficiary (IAS 20 treatment, see below).

#### 4.10.9 Accounting treatment (Chartered Accountant's ruling — build it this way)
- **Land held for sale or development is inventory under IAS 2, not PPE.** Parcel acquisition cost plus capitalisable servicing and development costs sit in a `Land & Housing Stock (WIP)` inventory account, transfer to `Stands Available for Sale` on completion of servicing, and release to **cost of sales on transfer of control** to the beneficiary — not on allocation, and not on offer.
- **Land and buildings the council holds to earn rentals** = investment property (IAS 40) or PPE (IAS 16) depending on policy — keep them out of the inventory pool entirely and out of the "stands available" reports.
- **Revenue recognition (IFRS 15)**: determine the point at which control of the stand passes (usually signature of the agreement of sale and giving of occupation, not final payment). Recognise the sale then, raise the full receivable, and treat the instalment arrangement as a **financing component** — discount the receivable where the term is long, and recognise interest income over the term rather than inflating sale revenue.
- **Instalment debtors** go to a dedicated AR control account per tenure type, with **IFRS 9 expected credit loss** provisioning by arrears bucket and by scheme — housing debtors behave nothing like trade debtors and must be provided for separately.
- **Deposits, rental deposits and part-payments on unaccepted offers are liabilities**, never revenue, until the triggering event. Forfeited deposits become other income on forfeiture approval.
- **Rental income** is straight-lined over the lease term where escalations are fixed (IFRS 16 lessor, operating lease).
- **Development/endowment levies and transfer fees** are revenue when the performance obligation is satisfied; connection fees may be deferred where they relate to a service still to be delivered.
- **Government grants and housing subsidies** (IAS 20): recognise in line with the expenditure they compensate; deferred income where conditions are outstanding; disclose unfulfilled conditions.
- **Compensation payable on compulsory acquisition** is a provision once the obligation is probable and measurable (IAS 37), not on payment.
- **Repossessions**: derecognise the receivable, recognise the stand back into inventory at the lower of previous carrying amount and net realisable value, take the difference (net of forfeitures and occupation rental charged) to P&L, and disclose it — repossession gains are a sensitive number and must be separately reportable.
- Every one of the above posts through the §3.2 posting engine. No module-local journals.

#### 4.10.10 Integration points
- → **§4.9 Municipality**: on title registration or on allocation (configurable trigger), create or update the billable property, link owner as the ratepayer, start rates and service charges pro-rata from the effective date, and pull live clearance status for transfers.
- → **§4.5 AR**: beneficiary, tenant and cooperative accounts, statements, ageing, dunning.
- → **§4.12 Job Costing**: servicing and construction contracts feed actual cost per scheme and per stand.
- → **§4.3 Procurement**: land acquisition, survey, conveyancing and servicing contracts procured through the normal requisition→PO→approval chain — land acquisition above a threshold should require the highest authorisation band in the system.
- → **§4.16 Service Desk**: tenant fault reports and applicant enquiries.
- → **§6 Audit** and **§7 Workflow**: allocations, ownership changes, price changes, registration-date changes, block placement/lifting and repossessions are all approval-routed and hash-chained.
- → **GIS hook**: store coordinates/polygons (PostGIS-ready columns) and expose an `IMapProvider` so stand maps, scheme layouts and ward overlays can be plotted later without a schema change.

#### 4.10.11 Housing-specific permissions (add to the §5 grid)
`Housing.LandParcel · Housing.Acquisition · Housing.Subdivision · Housing.Stand · Housing.Pricing · Housing.WaitingList · Housing.ApplicantVerify · Housing.RegistrationDate · Housing.AllocationRun · Housing.AllocationApprove · Housing.Ballot · Housing.Offer · Housing.Agreement · Housing.InstalmentSchedule · Housing.OwnershipChange · Housing.OwnershipApprove · Housing.Caveat · Housing.TitleDeed · Housing.Repossession · Housing.Tenancy · Housing.RentRun · Housing.Eviction · Housing.Scheme · Housing.Cooperative · Housing.InformalSettlement`
with the standard operation flags plus `Authorise-Level-1..5`. **Segregation of duties pairs to pre-load as violations:** create applicant + approve allocation; allocate stand + approve ownership change; set price + approve agreement; place caveat + lift caveat; verify clearance + approve transfer.

### 4.11 School Management & Fee Billing
- Students, guardians, classes/forms/streams, terms/semesters, academic years, enrolment and withdrawal with pro-rata.
- **Fee structures** per class/grade/term/boarding status, with compulsory and optional items (tuition, levy, boarding, transport routes, uniform, exam fees, trips).
- Discounts/bursaries/scholarships: % or value, sibling discounts, staff-child discounts, sponsor-paid accounts (a sponsor is billed for N students), with approval and expiry.
- **Billing run** per term, invoice per student, statements to guardians, arrears list per class (with a "do not disclose to child" flag on sensitive accounts).
- Payments: per student, per guardian, sponsor payments split across students, prepayment/credit balances carried to next term.
- Deferred income recognition across the term.
- Related: tuck shop (POS), boarding stores, transport route billing, hostel allocation, uniform shop stock.

### 4.12 Construction / Job Costing / Project Accounting
- Job/project/contract hierarchy: contract → phase → cost head/activity → transaction, with a full WBS.
- **BOQ import** and budget by cost head; revised budgets with variation orders (VOs) tracked separately.
- Costs captured from: purchase invoices, stock issues, timesheets/labour, plant & equipment usage (internal hire rates), subcontractor certificates, overhead allocation.
- **Progress valuations / interim payment certificates**: measured work, materials on site, VOs, less previous certificates, less **retention** (with retention release schedule and defects liability period), less advances recovery — producing a certificate document and the corresponding AR posting.
- **Subcontractor ledger**: subcontract orders, work certified, retention held, WHT/contractor tax deduction, payment certificates.
- WIP and revenue recognition (cost-to-cost %), contract asset/liability, provision for foreseeable losses.
- Plant register with hire rates, utilisation, maintenance schedules, fuel and running-cost capture.
- Job profitability with committed costs and cost-to-complete forecasting.

### 4.13 Sales Order Processing / Distribution
- Quotation → Sales order → Picking list → Delivery note/despatch → Invoice, with back orders, part despatch, allocation/reservation of stock, drop-shipment, and order amendment history.
- Delivery scheduling, vehicle loads, proof-of-delivery capture, route planning (basic), delivery charges by zone/weight/volume.
- Customer contracts and call-off orders.

### 4.14 Fixed Assets
- Asset register: class, location, custodian, cost, components, acquisition (from AP invoice or capitalised WIP), barcode/tag, insurance, warranty, images.
- Depreciation: straight line, reducing balance, units of production, per-asset or per-class, monthly run posting to GL, mid-month/mid-year conventions, part-period on acquisition/disposal.
- Revaluation (IAS 16), impairment (IAS 36), disposal with profit/loss, transfers between locations/cost centres, CWIP → asset capitalisation, asset verification/count with variance report.

### 4.15 Human Resources & Payroll

> **Specified as a Payroll Officer would specify it.** Payroll is the one module where a mistake is personal: an employee who is short-paid does not file a support ticket, they stand at the counter. It runs to an immovable deadline, it handles the most confidential data in the business, and it is audited by the revenue authority, the pension fund and the auditors. Therefore: **the calculation must be transparent and reproducible, the run must be reversible until it is committed and immutable afterwards, and a payroll officer must never be able to process their own payslip.**

#### 4.15.1 Employee master & HR core
- **Personal**: employee number (sequence per company), names as they appear on the national ID (this is what the bank and the tax authority match on), national ID/passport, date of birth, gender, marital status, nationality, work permit/visa with expiry alert, physical and postal address, contacts, next of kin and beneficiaries, photograph.
- **Employment**: engagement date, contract type (permanent / fixed-term / contract / casual / seasonal / intern / consultant), contract start and end with expiry alerts, probation period and confirmation, notice period, grade and notch/step, job title and position, **department, cost centre, project/fund and branch** (this is what drives the payroll journal), reporting line, union membership, shift pattern, pay group and pay frequency.
- **Statutory & scheme identifiers**: tax number, social security number, pension fund and member number, medical aid scheme, member number and dependants, workers' compensation class.
- **Bank details**: account name, number, branch, bank, currency, and **split-pay instructions** (e.g. 60% to bank A, 40% to mobile money). **Any change to bank details is a dual-authorisation workflow event with a mandatory audit record and a "bank details changed this period" exception report** — this is the single most common payroll fraud.
- HR records: qualifications, training, appraisals, disciplinary and grievance records, warnings with expiry, medical records flagged restricted, assets issued (laptop, phone, tools, uniform, keys).
- **Establishment / post management** (essential for councils and schools): approved post register, grading structure, salary scales with notches and annual increment rules, posts filled vs vacant, acting appointments, and a block on engaging an employee against a post that does not exist or is already filled.

#### 4.15.2 Pay element engine (formula-driven — never hardcode a single allowance)
- A **pay element** is a configured record, not code: code, name, type (`Earning | Deduction | EmployerContribution | Provision | Memo/Information`), calculation basis (fixed amount, % of basic, % of gross, % of a named element group, rate × units, formula expression, table lookup, balance-driven), calculation **sequence number** (order matters — a pension calculated on basic must run before a tax that is calculated after pension relief), and rounding rule.
- Per element flags: **taxable / non-taxable**, **pensionable / non-pensionable**, subject to social security, subject to union dues, included in overtime base, included in leave-pay base, included in terminal-benefit base, pro-rated for part-period, arrears-able, currency, ceiling and floor, employer/employee split, and **GL account mapping by department/cost centre** (with a fallback default).
- Reuse the §13.1 **expression engine** for element formulas so payroll formulas are written, tested and audited the same way report expressions are, with a "calculate against a sample employee" preview.
- Standard elements to ship seeded: basic salary, housing allowance, transport allowance, acting allowance, responsibility allowance, shift and night allowance, standby and call-out, **overtime at configurable multipliers (1.5×, 2×, public holiday)**, commission (from the POS/sales agent module), bonus and 13th cheque, backpay, leave pay, leave encashment, gratuity, severance, notice pay; and on the deduction side: tax, pension, social security, medical aid, union dues, **staff loans and advances**, **garnishee and court orders** (with priority ordering and protected-earnings rules), funeral scheme, savings scheme, **staff purchases (posted from the POS staff-account tender)**, **council/employer housing rent (posted from §4.10.7)**, and salary overpayment recovery.
- **Order-of-deduction and net-pay protection**: a configurable minimum take-home (absolute or % of gross); when deductions would breach it, the engine suspends lower-priority deductions and carries them forward as arrears, and reports exactly what it suspended and why.

#### 4.15.3 Tax and statutory calculation
- **Effective-dated tax tables**: bands, rates, thresholds, per frequency (monthly/weekly/annual), per currency, with full history so a prior-period recalculation uses the table that was in force then.
- **Cumulative (year-to-date) PAYE** and non-cumulative options; mid-year joiners and leavers; tax-year straddling a fiscal year.
- Tax credits and rebates (elderly, disabled, medical-aid contribution credit, dependant credits) as configurable, effective-dated records.
- **Taxable benefits**: motor vehicle benefit (by engine capacity or cost bands), housing benefit, **loan benefit at a deemed interest rate**, school fees, medical, and any element flagged as a benefit — valued by rule, taxed, and shown on the payslip as a memo item.
- **Bonus/annual payment tax spreading** (annualise-and-apportion) rather than taxing a bonus at the marginal rate of one month.
- **Multi-currency payroll taxation**: where an employee is paid partly in a hard currency and partly in local currency, compute the tax **per currency on the correctly apportioned tax-free threshold and bands**, produce separate statutory returns per currency, and show both on one payslip with the exchange rate used. Get this wrong and the client gets a tax assessment — treat it as a first-class requirement, not an edge case.
- Surcharges/levies computed on tax (e.g. a health or AIDS levy), employer levies (skills/manpower development, workers' compensation), and social security with earnings ceilings.
- Statutory returns generated as data plus documents: monthly tax remittance return, annual employee tax certificates, social security schedule, pension schedule, workers' compensation return, skills levy return — each format-configurable through the report designer because these forms change.

#### 4.15.4 Leave & attendance
- Leave types: annual, sick (with full-pay/half-pay tiers and rolling entitlement windows), maternity, paternity, compassionate, study, unpaid, special, and time-off-in-lieu.
- Accrual rules per type and per grade: monthly accrual, on anniversary, pro-rata on engagement, service-band uplifts, **carry-forward caps and expiry**, maximum accumulation, and negative-balance policy.
- Applications and approvals through the §7 workflow, with team calendars, coverage checks and cancellation.
- **Leave liability provision (IAS 19 short-term benefits)**: value untaken leave at current rates each period and post the movement to the provision — auditors ask for this every year and clients almost never have it.
- Leave encashment and leave-pay advance (pay the leave in the month before it is taken, recover in the month it falls).
- **Attendance/time**: clock devices or timesheet entry, shift rosters, absence and lateness capture, overtime request-and-approve before it can be paid, and **timesheet hours flowing into §4.12 job costing** so labour lands on the right job.

#### 4.15.5 Loans, advances and third parties
- Loan register: amount, interest (or interest-free with deemed-benefit taxation), instalment, term, start, balance, settlement, suspension while on unpaid leave, and automatic deduction each run.
- Salary advances with recovery in the same or following period.
- **Third-party payments**: after each committed run, automatically raise the AP invoices or payment requests for the tax authority, pension fund, medical aid, union, garnishee creditors and savings schemes, so the money is actually remitted through §4.6/§4.7 and reconciles back to the deduction totals.

#### 4.15.6 The payroll run
A strict, resumable state machine:
`Open period → Capture variables → Calculate → Validate → Review → Approve → Commit → Post to GL → Pay → Close`
- **Variable input**: overtime, absences, once-off earnings and deductions, new engagements, terminations, salary changes — captured individually or imported from a validated Excel/CSV template with a preview-and-error screen.
- **Calculate is re-runnable any number of times before commit.** Each calculation stores a full trace per employee per element ("basic 1,200.00 × 0.075 = 90.00 pension") so the officer can explain any figure to any employee without opening a spreadsheet.
- **Pre-commit validation / exception report** — the payroll officer's safety net. Block or warn on: negative net pay, zero net pay, net pay varying more than N% from last period, employees with no bank details, **duplicate bank account numbers across employees**, duplicate national IDs, employees with no tax number, terminated employees still in the run, employees in the previous run missing from this one, negative leave balances, deductions suspended by net-pay protection, salary below the grade minimum or above the maximum, contract expired, and bank details changed since the last run.
- **Approval**: maker-checker, with the approver seeing the totals and the exception report, not just a button. Above a configurable variance threshold, require a second approver.
- **Commit** freezes the run: no edits, ever. Corrections are made by a **supplementary/adjustment run** or as backpay in the next period, both of which are visible and reportable. A commit can only be reversed by a formally approved **payroll reversal** that writes a reversing journal — never by deletion.
- **Parallel run mode**: run the new system alongside the legacy one for a month or two and produce a line-by-line comparison report. Insist on this for every payroll go-live.
- Multiple payrolls per company (monthly staff, weekly wages, casuals), multiple pay groups, and the ability to run them independently and consolidate for reporting and statutory returns.

#### 4.15.7 Payment and payslips
- **Bank EFT files** in configurable per-bank layouts (define the format as a template, not as code), with a control total and a hash for the bank's verification; separate files per currency and per bank.
- Mobile money payment files; cash payroll with a **denomination breakdown report** per pay point; cheque payments.
- **Payslip**: designed in the report designer, printable, and emailable as a **password-protected PDF** (password rule configurable — national ID, date of birth, or employee-set). Shows: earnings, deductions, employer contributions, taxable benefits as memo, net pay by payment method and currency, leave balances, loan balances, year-to-date totals, and the tax computation summary.
- Employee self-service hooks in the API: payslip history, tax certificate, leave balance and application, personal-detail change request routed for HR approval.

#### 4.15.8 Terminations
- Termination types: resignation, dismissal, retrenchment, retirement, end of contract, death, abscondment — each with its own entitlement rules.
- **Final pay computation**: pro-rata basic, notice pay or pay in lieu, **leave encashment**, pro-rata bonus, gratuity, severance per the statutory formula or contract, less loan balances, less unreturned assets, less any overpayment.
- **Tax on terminal benefits** per the applicable concession rules, with the calculation shown.
- **Clearance checklist** before the final payment is released: assets returned, staff account settled (from POS), loans settled or written off with approval, handover signed, housing vacated (link to §4.10.7 if the employer houses staff).
- Certificate of service, final payslip, tax certificate, and automatic removal from the next run with an audit record.

#### 4.15.9 Accounting treatment (Chartered Accountant's ruling)
- The payroll journal posts gross cost by **department / cost centre / project / fund**, not as one lump: Dr salary and allowance expense accounts, Dr employer contribution expense, Cr net pay control (a liability cleared by the bank payment), Cr each statutory and third-party liability account.
- **Net pay control and each statutory liability account must reconcile to zero** after the payment run — ship the reconciliation report (PR-series) and make it part of the month-end checklist.
- Accrue unpaid salaries at period end where the pay date falls after the cut-off.
- **IAS 19**: leave provision and bonus provision as short-term benefits; gratuity and long-service awards as other long-term benefits with a discounted provision; defined contribution plans expensed as incurred; if any client runs a defined benefit plan, that is an actuarial input, not a calculation this system performs — capture and disclose it, do not compute it.
- Capitalise payroll cost into WIP where staff work on a construction contract or on self-constructed assets, via the timesheet link.
- Multi-currency payroll follows §3.3: each currency's postings at that currency's rate, with the FX difference on settlement to the FX accounts.

#### 4.15.10 Security — treat payroll as a walled garden
- Own schema (`pay`), own permission domain, and **salary, bank details and national ID encrypted at rest** (column-level encryption with keys held outside the database).
- Row-level scoping so a branch HR officer sees only their branch, and **field-level restriction** so a line manager can approve leave without ever seeing pay.
- **Hard SoD rules, pre-loaded as violations**: process payroll + approve payroll; maintain employee master + approve payroll; change bank details + approve payment run; **and an absolute block on any user calculating, approving or committing a run that includes their own employee record** (the run refuses to commit and names the conflict).
- Payroll audit trail is separate and visible only to a payroll-audit role; payslip reprints, exports and report prints are all logged (a payroll export is a data-breach risk, so record every one).
- Permissions to add to the §5 grid: `Payroll.Employee · Payroll.BankDetails · Payroll.PayElement · Payroll.TaxTable · Payroll.SalaryChange · Payroll.Variables · Payroll.Calculate · Payroll.Validate · Payroll.Approve · Payroll.Commit · Payroll.Reverse · Payroll.Payslip · Payroll.PayslipReprint · Payroll.BankFile · Payroll.StatutoryReturn · Payroll.Termination · Payroll.Loan · Payroll.Leave · Payroll.LeaveApprove · Payroll.Establishment · Payroll.ViewSalary · Payroll.Reports`

*(Build the employee master, establishment and timesheets early — §4.12 job costing and §4.18 budgeting both depend on them even if the payroll calculation itself lands in a later phase.)*

### 4.16 CRM & Service Desk (lightweight, recommended)
- Leads, opportunities, activities, quotations link.
- **Ticketing** for municipalities (fault reports: burst pipe, no water, refuse not collected) linked to property and service, with SLA timers, assignment, resolution, and a public reference number. This single feature sells the municipal module.

### 4.17 System Manager
Companies, branches, users, roles, groups, permissions, periods, currencies, rates, number sequences, tax codes, document layouts, printers per workstation, terminal profiles, email/SMS gateways, scheduled jobs, backup configuration, data-integrity checks, audit trail enquiry, licence status, and a **Data Health dashboard** (orphan records, unbalanced batches, control account variances, negative stock, unposted batches).

### 4.18 Budgeting, Planning & Commitment Control

> **Specified from the Finance Office.** A budget is not a column on a report — it is a document with an author, a version, an approval, a legal or board mandate, and a life after the year ends. Two rules govern this module and must never be compromised: **(1) every department prepares, owns and answers for its own numbers, and (2) no budget is ever overwritten or deleted.** Every version ever submitted, negotiated, cut, approved, revised or superseded is retained and remains fully reportable years later. When the auditor asks "what was originally approved for the Works Department in 2024, and who reduced the training line by 40% in the revision?", the system answers in one screen.

#### 4.18.1 Budget structure
- A **budget** is a header plus lines. Header: name, fiscal year (or multi-year span), **budget type/scenario**, **version number**, status, preparing entity (company/department), owner/budget holder, currency and budget exchange rate, basis notes, and approval references.
- A **budget line** keys on the full posting dimension set so it compares directly to actuals with no mapping layer: `Account + Cost Centre + Department + Project/Job + Fund/Vote + Branch`, with an amount per period bucket for the whole year, plus quantity and rate where the line is driver-based.
- **Budget types/scenarios**, all coexisting for the same year: `Original/Approved · Supplementary/Revised · Reforecast (Q1/Q2/Q3) · Draft/Submission · Base case · Best case · Worst case · Zero-based · Rolling forecast · Capital budget · Cash budget · Project budget · Establishment/staff budget`.
- **Revenue budgets are first-class**, not an afterthought — councils budget rates, service charges, licence fees and housing sales; schools budget fee income; retail budgets sales by department and store. Every budget report must run on income as readily as on expenditure.
- **Multi-year budgeting (MTEF-style)**: a three- or five-year expenditure and revenue framework where year 1 is the appropriated budget and years 2–n are indicative, with roll-forward each year.
- Multi-currency: budget in a chosen currency at a **budget rate** held on the header, with base-currency equivalents maintained so consolidated reporting works; FX-driven variance separately identifiable from volume and price variance.

#### 4.18.2 Departmental preparation (multiple departments, working in parallel)
- **Every department is a preparation unit** with its own budget holder, its own workspace, its own submission, and its own deadline. Departments cannot see or edit each other's numbers unless granted; Finance sees everything.
- **Organisation hierarchy** for consolidation: cost centre → department → division → directorate → company → group. A budget rolls up the tree automatically, and any level can be reported on, approved at, or locked independently.
- **Top-down targets and bottom-up submissions together**: Finance issues a target/ceiling per department; departments prepare against it; the system shows the **gap between target and submission** at every level and will not let Finance forget an unreconciled gap.
- **Preparation methods** selectable per line or per department: zero-based, incremental (prior year ± %), actuals-based (last year's actuals ± %, or annualised part-year actuals), driver-based (`volume × rate` — learners × fee, properties × tariff, tonnes × price, headcount × average cost), activity-based, and manual.
- **Driver/assumption register**: inflation rate, exchange rate, salary increment %, headcount, enrolment, tonnage, connection growth — stored as named assumptions so a budget line references the assumption rather than a hardcoded number, and **changing one assumption re-computes every dependent line** with a before/after impact preview.
- **Spreading profiles**: even, prior-year seasonality, manual per period, working-day weighted, S-curve (for capital projects), custom weighting sets — applied per line, re-applied on amount change.
- **Establishment-driven staff budget**: build the payroll budget from §4.15's approved post register — posts × grade × notch × scale, plus increments, vacancy factor, and employer contributions — rather than typing a number. When the salary scale changes, the staff budget re-computes.
- **Capital budget**: project-linked, with phasing across years, funding source, commitment profile, and **year-end carry-forward of unspent approved capital** (approved as a separate act, not automatic).
- Excel round-trip: export a locked template per department, import back with validation, difference preview and an error report. Departments will use Excel; make it a controlled path rather than a shadow process.

#### 4.18.3 Versioning and retention — never lose a budget
- **Immutable versioning.** Editing an approved budget is impossible. Any change creates a **new version**, with the prior version retained in full, sealed, and permanently reportable. Versions are numbered and carry: created by, created date, reason for the version, source version it was copied from, and approval references.
- **Status lifecycle**: `Draft → Submitted → Under review → Returned for amendment → Recommended → Approved → Active → Superseded → Archived → Locked`. Only one version per type may be `Active` at a time; superseding is an explicit, approved act.
- **Approval snapshot**: on approval the full line set is hashed and sealed (joining the §6 audit hash chain), so an approved budget can be proved unaltered years later.
- **Retention is permanent by default.** Budgets are never deleted by any user at any permission level; old fiscal years remain queryable, comparable and reportable without restore. Archiving moves storage, never availability. If a client demands purging, it is a vendor-key operation, logged, and never exposed in the UI.
- **Cross-year comparison out of the box**: report any account/department across N prior years' budgets *and* actuals side by side — original vs revised vs actual for each year — which is exactly the table finance committees ask for.
- **Version comparison**: any two versions, any two years, any two scenarios, line by line, showing movement and % change, with a **"what changed and who changed it"** drill-down. This is how a department proves its training line was cut in review rather than under-requested.
- Full change log on every line (old value, new value, user, timestamp, reason) inside a version's draft life, and on the version itself thereafter.

#### 4.18.4 Workflow, negotiation and appropriation
- Preparation runs through the §7 workflow: department prepares → budget holder submits → Finance reviews (can annotate, propose, return) → budget committee → executive → **board or council approval with the minute/resolution reference recorded on the header** → activation.
- **Negotiation rounds** are preserved: each round is a version, with Finance's proposed figure and the department's requested figure both retained alongside comments, so the trail of cuts is never lost.
- **Supplementary budgets / additional appropriations**: a formal document that adds to or reduces an approved budget, separately approved, separately reported, and never merged silently into the original.
- **Virements (transfers between budget lines/votes)**: request, source line, target line, amount, justification, approval band by value and by whether the transfer crosses departments or crosses expenditure categories (some transfers a manager may make, some need the council). Every virement is a retained document; the **virement register** reconciles original + supplementary ± virements = current approved budget for every line.
- Deadline management: preparation calendar, per-department deadlines, reminders, and a submission-status board Finance watches during budget season.

#### 4.18.5 Commitment control and budget enforcement (where budgeting earns its keep)
- **Available balance** on any line, live:
  `Available = Approved (original + supplementary ± virements) − Actuals − Commitments (open POs) − Requisitions in flight − Manual reservations`
- Enforcement policy per department, per account category and per value band: `Allow · Warn · Require approval · Block`. Applied at **requisition submission, requisition approval, PO raising, and invoice posting** — checked at every gate, because a budget checked only at requisition is a budget that gets overrun at invoice.
- **Reservations/earmarking** for known future spend that has no PO yet.
- **Budget holder notifications** at configurable utilisation thresholds (e.g. 75%, 90%, 100%), plus a monthly budget-holder statement pushed by email.
- **Vote book / fund accounting** for councils and grant-funded bodies: appropriation by vote, expenditure against vote, commitments against vote, unexpended balance, and the classic **vote book report** — plus a hard rule that expenditure cannot be posted against a vote with no appropriation.
- Year-end: commitments outstanding at year end either lapse or carry forward (per policy, approved), and the carry-forward is itself an auditable document.

#### 4.18.6 Monitoring, variance and forecasting
- **Budget vs Actual vs Commitment vs Available**, at any level of the hierarchy, for any period, YTD or full year, with drill-down from the variance straight to the transactions that caused it.
- Variance analysis with **decomposition** where drivers exist: split the total variance into volume, price/rate and FX components rather than reporting one unexplained number.
- **Variance commentary**: budget holders record explanations against material variances (threshold by value and %), and the commentary prints with the management accounts — turning the month-end pack from a spreadsheet exercise into a record.
- **Rolling forecast / reforecast**: take actuals to date + forecast for remaining periods = full-year forecast, versioned like any other budget, compared against the approved budget, with forecast-vs-forecast movement reporting.
- **Cash budget**: convert the income and expenditure budget into expected receipts and payments using debtor/creditor collection and payment profiles, and compare to the §4.7 cash flow forecast.
- Phasing re-profile: move approved budget between periods within the year (a timing change, not a value change) with its own approval and audit, kept distinct from virements.

#### 4.18.7 Accounting and integration notes
- Budgets **do not post to the general ledger**. They live in their own schema (`bud`) and are joined to actuals by the posting dimensions at report time. Never let a budget figure contaminate a trial balance.
- Commitments are tracked in a **commitment/encumbrance ledger** that is reported alongside the GL but is not part of double entry, and is reconciled to open POs and open requisitions nightly with an exception report.
- Integration: → §4.3 Procurement (the budget check at every gate), → §4.4 GL (actuals), → §4.15 Payroll (establishment-driven staff budget and actual payroll cost), → §4.12 Job Costing (project budgets and cost-to-complete), → §4.9/§4.10 (revenue budgets for rates, services and stand sales), → §13 Reporting (every report below).
- Permissions for the §5 grid: `Budget.Prepare · Budget.SubmitOwnDepartment · Budget.ViewOwnDepartment · Budget.ViewAllDepartments · Budget.Review · Budget.Recommend · Budget.Approve · Budget.Activate · Budget.Supersede · Budget.Supplementary · Budget.Virement · Budget.VirementApprove · Budget.Rephase · Budget.Reservation · Budget.OverrideBlock · Budget.Assumptions · Budget.Target · Budget.Forecast · Budget.Commentary · Budget.Archive · Budget.Reports`
  **SoD pairs to pre-load:** prepare + approve; submit + activate; raise requisition + override budget block; virement request + virement approval.

---

## 5. SECURITY, PERMISSIONS, ROLES, GROUPS, USERS

### 5.1 Model
```
User ──< UserGroupMember >── Group ──< GroupRole >── Role ──< RolePermission >── Permission
User ──< UserRole >── Role                         (direct assignment also allowed)
User ──< UserPermissionOverride >  (explicit Grant or Deny; Deny always wins)
```
- **Permission** = `Module.Feature` (e.g. `Sales.Invoice`, `Inventory.StockAdjustment`, `Municipal.BillingRun`) with a set of **operation flags**.
- **Operation flags (the checkbox grid columns):**
  `View | Create | Edit | Delete | Print | Export | Post | Approve | Void/Reverse | Authorise-Level-1..5 | Override-Price | Override-Discount | Override-Credit-Limit | ViewCost | ViewMargin | Reprint | BackDate`
  Add flags where a module needs them; keep the grid generic.
- **The permission UI is a grid**: rows = features (grouped in a tree by module), columns = operation checkboxes, with tri-state parent checkboxes, "select all in row/column", filter box, and a **compare/copy from another role** function.
- **Scope rules** (row-level security): a role assignment is scoped to companies, branches, warehouses, tills, cost centres, departments, wards/zones, and job/projects. Enforced server-side in query filters — never only in the UI.
- **Field-level** sensitivity: mark fields (cost price, margin, salary, customer credit limit) as restricted; the API strips them for unauthorised users.
- **Segregation of duties matrix**: define conflicting permission pairs (e.g. create supplier + approve payment run); the system warns at role-assignment time and produces an **SoD violation report** for auditors.
- **Supervisor override at POS**: a permission-gated action prompts for a supervisor PIN and records an override audit record (who authorised what, for whom, on what transaction).

### 5.2 Authentication & session
- Argon2id hashing, password policy (length/complexity/history/expiry/lockout), forced change on first login, optional TOTP 2FA for back-office roles, PIN login for POS, session timeout with lock screen, concurrent-session policy, named-user licence enforcement.
- Login audit: successes, failures, lockouts, source machine/IP.

---

## 6. AUDIT TRAIL

Two complementary layers — build both.

**A. Transaction audit trail (accounting):** every posted transaction keeps its source module, document type, document number, batch number, posting date, entry date, user, terminal, and an immutable link to the source record. Reversals reference the original. Nothing is ever updated in place.

**B. Change audit log (technical):** an append-only table (`audit.change_log`) capturing:
`id, occurred_at (timestamptz), company_id, user_id, user_name, terminal, ip_address, session_id, module, entity_type, entity_id, action (Insert|Update|Delete|Login|Logout|Print|Export|Post|Approve|Reject|Void|Override|ConfigChange|PermissionChange|LicenceChange), old_values (jsonb), new_values (jsonb), changed_fields (text[]), reason, correlation_id, row_hash`

Implementation:
- EF Core `SaveChanges` interceptor for entity changes + explicit calls for non-entity events (prints, exports, logins, overrides).
- Postgres triggers as a **second net** on the most sensitive tables (gl postings, users, roles, permissions, prices, tariffs, meters, licence) so even direct SQL is caught.
- **Tamper evidence:** each row stores `row_hash = SHA256(previous_row_hash || row_payload)` forming a hash chain per company; a verification routine reports any break.
- Audit is **write-only for everyone**; no UI or API can delete or update it. Retention/archival via a partition-detach process requiring the licence-holder's key.
- Audit enquiry screen: filter by user/date/module/entity/action, side-by-side old vs new value diff, export to PDF/Excel, and a "who touched this record" button on every form.

---

## 7. WORKFLOW & APPROVALS ENGINE (generic, reused everywhere)

One engine serving requisitions, POs, journals, credit notes, write-offs, payment runs, bank-detail changes, discounts above threshold, tariff changes, master-data changes, municipal write-offs, and school bursaries.

- Definition: document type → stages → rules (condition expression) → approvers (user/role/group/dynamic e.g. "requester's manager") → quorum → SLA/escalation → notifications.
- Runtime: instance, current stage, actions, comments, attachments, full history, snapshot of the document at each decision.
- Delegation, out-of-office, bulk approve, mobile-friendly approval list, email approval link (signed token) optional.
- **Approval reports** for auditors: what was approved, by whom, how quickly, what was overridden.

---

## 8. NOTIFICATIONS & COMMUNICATIONS
- In-app notification centre, email (SMTP/Graph), SMS gateway abstraction, WhatsApp/Telegram hook (optional).
- Templates with merge fields per document type, per language, previewable.
- Bulk despatch jobs with delivery status tracking and retry (statements, bills, dunning letters, fee reminders).

---

## 9. DATABASE DESIGN GUIDANCE (PostgreSQL)

- Connection for development: `Host=localhost;Port=5432;Database=meridian;Username=postgres;Password=root`. **Store it in `appsettings.Development.json` only**; production connection strings come from encrypted config / environment variables. Never commit a production password.
- **Schemas per module**: `sys`, `sec`, `audit`, `gl`, `ar`, `ap`, `cb`, `inv`, `pos`, `pop`, `sop`, `mun`, `hse`, `sch`, `job`, `fa`, `hr`, `pay`, `bud`, `rpt`, `lic`.
- Primary keys: `bigint` identity for transactional tables; `uuid` for anything synced from offline terminals (POS) to avoid collisions.
- Indexing: cover every foreign key; `pg_trgm` GIN on searchable text (item name/code, customer name, property address, student name); partial indexes on `is_active`, `status`; BRIN on large date-ordered transaction tables.
- **Partitioning** by company + year on: `gl.transaction_line`, `pos.sale_line`, `audit.change_log`, `mun.meter_reading`, `mun.bill_line`, `hse.ownership_history`, `bud.budget_line`, `pay.payslip_line`.
- Materialised views for heavy reporting aggregates (aged debt snapshot, stock valuation, sales by day/dept), refreshed by Hangfire, with a "last refreshed" stamp shown on the report.
- Use **`numeric`**, never `money`. Enforce `CHECK` constraints on sign conventions and status enums.
- DB-level guards: trigger preventing update/delete on posted GL lines; trigger asserting batch balance on batch close; trigger blocking manual journals to control accounts.
- Full-text search configuration with `unaccent` for names/addresses.
- Provide `db/sql/seed/` with: currencies, countries, tax codes, sample COA per vertical, permission catalogue, report definitions, document layouts, demo company.
- Backup/restore utility in System Manager: `pg_dump` scheduling, restore wizard, integrity verification, and an offsite copy hook.

---

## 10. INTEGRATION & EXTENSIBILITY
- **Module plug-in model**: each functional module registers itself (`IModuleDescriptor`: id, name, licence key, menu entries, permissions, migrations, report definitions, jobs). The shell builds navigation from licensed + permitted modules only.
- REST API with OpenAPI/Swagger for third parties; webhook publisher for key events (sale completed, invoice posted, bill generated, payment received).
- Import/export framework: CSV/Excel templates with validation preview and error report for every master data type and for opening balances.
- **Opening balance wizard**: migrate from a legacy system — customers, suppliers, stock (qty + cost), open AR/AP items, GL trial balance, properties/meters/stands/ownership history/students/employees with YTD payroll balances/prior-year approved budgets — with a balancing suspense account and a reconciliation report before go-live.

---

## 11. LICENSING TOOL (SEPARATE APPLICATION — VENDOR ONLY)

### 11.1 Two deliverables
1. **`Meridian.Licensing.Generator`** — a standalone WPF/console app that **you, the vendor, run**. It is never shipped to customers and never in the same installer. Holds the **private key**.
2. **`Meridian.Licensing.Core`** — a small library referenced by the ERP that only **validates** licences using the embedded **public key**.

### 11.2 Cryptography
- **Ed25519** (or RSA-2048 PSS if you prefer tooling familiarity) signatures over a canonical JSON payload. The licence file is `payload_base64url.signature_base64url` (a compact signed token).
- Private key stored in the generator, protected by a passphrase (PBKDF2/Argon2 → AES-GCM), with an export/backup routine and key-rotation support (licences carry a `kid`; the ERP embeds a small set of trusted public keys).
- The ERP **never** sends a licence to a server to validate; validation is fully offline and deterministic.

### 11.3 Licence payload
```jsonc
{
  "licence_id": "uuid",
  "kid": "key-2026-01",
  "issued_at": "2026-01-15T00:00:00Z",
  "customer": { "id": "CUST-0142", "name": "…", "contact": "…" },
  "product": "Meridian ERP",
  "edition": "Standard|Professional|Enterprise|Municipal|Education",
  "licence_type": "Perpetual|Subscription|Trial|Demo|Evaluation|Rental",
  "valid_from": "2026-01-15",
  "valid_to": "2027-01-14",            // null = perpetual
  "grace_days": 14,
  "hard_stop": true,                    // false = degrade to read-only instead of blocking
  "machine_binding": {
    "mode": "None|Machine|Server|Domain",
    "fingerprints": ["sha256:…"],       // allow N fingerprints for server failover
    "allow_rebind_count": 2
  },
  "limits": {
    "companies": 3,
    "concurrent_users": 10,
    "named_users": 15,
    "pos_terminals": 4,
    "warehouses": 5,
    "properties": 50000,
    "stands": 25000,
    "students": 2000,
    "employees": 500,
    "budget_departments": 40,
    "transactions_per_year": null
  },
  "modules": [
    { "code": "GL",      "valid_to": "2027-01-14", "limits": {} },
    { "code": "AR",      "valid_to": "2027-01-14" },
    { "code": "AP",      "valid_to": "2027-01-14" },
    { "code": "POS",     "valid_to": "2026-07-14", "limits": { "terminals": 2 } },
    { "code": "MUNI",    "valid_to": "2027-01-14", "limits": { "properties": 50000 } },
    { "code": "HSE",     "valid_to": "2027-01-14", "limits": { "stands": 25000, "waiting_list": 100000 } },
    { "code": "BUD",     "valid_to": "2027-01-14", "limits": { "departments": 40 } },
    { "code": "PAY",     "valid_to": "2026-12-31", "limits": { "employees": 500, "payrolls": 3 } },
    { "code": "REPORT_DESIGNER", "valid_to": null }
  ],
  "features": ["MULTI_CURRENCY", "OFFLINE_POS", "API_ACCESS", "CONSOLIDATION", "HYPERINFLATION"],
  "support_expiry": "2027-01-14",
  "notes": "…"
}
```
**Per-module expiry is mandatory** — a customer can have GL perpetual and POS on a 6-month subscription. Timeframes must be arbitrary: days, months, years, or a specific end date; trials in days from first activation.

### 11.4 Generator app features
- Customer register (name, contact, sites), licence history per customer, reissue/renew/upgrade/downgrade, revoke.
- Module picker grid with per-module date and limit fields; edition templates that pre-tick modules.
- Machine fingerprint intake: customer runs a small **"Get Machine ID"** utility (ship it with the ERP, or a menu item in System Manager) that produces a request code; paste it into the generator.
- Output: `.lic` file, copyable activation string, printable licence certificate PDF, and a QR code for short keys.
- Bulk issue from CSV; expiry dashboard ("licences expiring in 30 days"); audit log of every licence issued (immutable, exportable).
- Offline activation challenge/response flow for locked-down sites.
- Optional online activation service (ASP.NET Core minimal API + Postgres) for self-service renewal — design the interfaces now, implement later.

### 11.5 ERP-side enforcement
- Validate at startup and every N minutes; cache the validated result in memory only.
- **Fingerprint** derived from a stable composite (motherboard UUID + machine GUID + primary NIC MAC + volume serial), hashed; tolerate one component change (fuzzy match ≥3 of 4) to avoid false lockouts after a NIC change.
- **Clock-rollback detection**: store a monotonic high-water-mark timestamp (encrypted, in DB and registry/file); if system time < high-water − tolerance, flag tamper and enter grace/read-only.
- Module gating: `ILicenceService.IsModuleLicensed(code)` drives menu visibility, API authorisation filters, and job scheduling. A locked module is **hidden**, and its API endpoints return 403 — do not rely on UI hiding alone.
- Limits enforced at the point of creation (companies, terminals, named users, concurrent sessions, properties, students) with clear messages ("Licence allows 4 POS terminals; 4 are registered. Deactivate one or contact your vendor.").
- Expiry behaviour: warning banner from T−30 days, daily nag from T−7, grace period after expiry, then either hard stop or **read-only mode** (view + print only, no posting) per the licence flag. **Never destroy or lock the customer's data.**
- Licence status screen: customer, edition, modules with expiry dates and colour-coded status, limits vs current usage, machine ID, and an "Install new licence" button.

---

## 12. MASTER DATA & CONFIGURATION THE VERTICALS NEED
Provide an **Industry Setup Wizard** at company creation: pick vertical(s) → seeds COA, tax codes, item categories, document layouts, POS terminal profile, permission roles, report set, and demo data. Verticals are additive (a council can run a POS for its clinic pharmacy, a housing department, a rates department and a rental stock portfolio from the same company record).

---

## 13. REPORT DESIGNER & REPORTING ENGINE (BUILD THIS PROPERLY — IT IS A HEADLINE REQUIREMENT)

The user wants Crystal Reports–class flexibility with **every field editable**. Do not bolt on a closed third-party designer. **Build an in-house banded report designer and engine** in `Meridian.Reporting` + `Meridian.Reporting.Designer`.

### 13.1 Report definition model (persisted as JSON in `rpt.report_definition`, exportable as `.mrpt` files)
- **Data**: named data sources (SQL query / view / stored function / API endpoint / in-memory dataset), parameters (typed, with default, prompt, list-of-values from a lookup query, cascading dependencies, multi-select, required/optional), master-detail relationships for sub-reports, and a designer-side **query builder** plus raw SQL editor with a field-discovery step (`SELECT … LIMIT 0` to read schema).
- **Bands**: `ReportHeader`, `PageHeader`, `GroupHeader[n]`, `Detail`, `DetailChild`, `GroupFooter[n]`, `PageFooter`, `ReportFooter`, plus `Overlay/Watermark`. Per-band: height, auto-grow/shrink, repeat on new page, keep-together, page break before/after, suppress-when expression, alternating row styling.
- **Elements**: text label, data field, expression field, rich text, image (static, from DB bytea, or from URL/path), line, rectangle, shape, page break, barcode (Code128, Code39, EAN-13, QR, PDF417), chart (bar, stacked, line, pie, donut, area, combo), cross-tab/matrix (row groups, column groups, measures, totals), sub-report, checkbox, signature block, table/grid element with per-column formatting, and a map/gauge (nice-to-have).
- **Every element property is editable in a property grid**: name, position (X/Y/W/H), anchoring/docking, font (family, size, bold/italic/underline, colour), alignment (h/v), padding, margins, border (per side, style, width, colour), background (solid/gradient), rotation, angle, z-order, visibility expression, format string (numeric, currency with currency-symbol binding, date, percentage, custom mask), null-value text, word-wrap, auto-shrink font to fit, hyperlink/drill-through target, tooltip, and **conditional formatting rules** (expression → style).
- **Grouping & sorting**: multi-level, group on field or expression, sort ascending/descending/custom, group headers/footers with aggregates, "repeat group header on new page", top-N filtering.
- **Aggregates**: `Sum, Avg, Count, DistinctCount, Min, Max, First, Last, Median, StdDev, RunningTotal, PercentOfTotal, RankOf` — scoped to detail/group/page/report, with reset rules.
- **Expression engine**: use `DynamicExpresso` or Roslyn scripting with a **sandboxed, curated function library**: math, string (`Left/Right/Mid/Replace/Trim/Upper/Proper/Format`), date (`AddMonths/DateDiff/EndOfMonth/FiscalPeriod`), logical (`If/Case/IsNull/Coalesce`), aggregate references, `NumberToWords` (essential for cheques and vouchers — multi-currency and multi-language), currency conversion helper `ToBase(amount, currency, date)`, and page/report variables (`PageNumber`, `TotalPages`, `PrintDate`, `UserName`, `CompanyName`, `CompanyLogo`, `Parameters.X`). No file/network/reflection access.
- **Page setup**: paper size (incl. custom and roll/receipt widths 58 mm/80 mm), orientation, margins, columns (multi-column labels), watermark, background image, duplex hints, printer tray selection per report.
- **Localisation**: label translations per report, RTL support, and per-company terminology overrides ("Stand" vs "Erf" vs "Property").

### 13.2 Designer UX (WPF)
- Drag-and-drop canvas with rulers, snap-to-grid, alignment guides, multi-select, group/ungroup, align/distribute toolbar, z-order controls, zoom, undo/redo stack, copy/paste across reports.
- **Field explorer** panel (data source tree: tables, columns with types, parameters, aggregates, variables) — drag a field onto the canvas to bind it.
- **Property grid** with search, grouped categories, and an expression editor dialog (with IntelliSense-style field/function completion and a live "evaluate against first row" preview).
- **Live preview tab** with real data and parameter prompting; page navigation; export from preview.
- Report templates/themes so a customer's house style (fonts, colours, logo, footer) applies across all reports in one click.
- **Version history** for report definitions with diff and rollback; "system" reports are read-only but can be **copied to a user version** — never let a customer edit a system report in place (upgrades would clobber it). Resolution order at runtime: user version → company version → system version.
- Import/export `.mrpt` files for sharing between sites.

### 13.3 Engine & output
- Render pipeline: definition + parameters + data → layout → paginated document.
- Renderers: **WPF FixedDocument** (on-screen preview + direct printing), **PDF** (QuestPDF/PdfSharpCore), **XLSX** (ClosedXML, with real columns not images), **CSV**, **HTML**, **DOCX** (optional), **plain-text/ESC-POS** (for receipt and dot-matrix printers — councils and schools still run dot-matrix).
- Viewer control with: page navigation, zoom, search-in-report, **drill-down/drill-through to source documents**, export button, email button, print with printer/tray/copies selection, and a "reprint" audit record.
- **Scheduling**: run reports on a schedule (Hangfire), deliver by email/folder/printer, with a distribution list and a delivery log.
- **Bursting**: split one report run by a key (customer / property owner / student / supplier / branch) into N documents, each emailed to the right recipient. **This is how bulk statements and bills are issued — it is required, not optional.**
- **Performance**: stream large result sets, cap in-memory rows, pre-aggregate in SQL, show progress with cancel, and cache parameter lookups.
- **Security**: report-level permissions; data filtered by the running user's row-level scopes (a branch manager's report must not include other branches); watermark "CONFIDENTIAL/DRAFT" driven by expression; every print/export written to the audit log.

---

## 14. THE REPORT CATALOGUE — SHIP ALL OF THESE

Create `docs/reports/CATALOGUE.md` listing each report with: code, name, module, purpose, parameters, columns, grouping, totals, and output formats. Implement them as seeded report definitions. **Common parameters** unless stated: Company, Branch/Site, Date or Period range, Currency (transaction / base / reporting), Cost Centre, Department, and "Include unposted Y/N".

> Design rule: every financial report must be runnable in **transaction currency, base currency, or reporting currency**, and must state at the top which rate basis was used.

### A. General Ledger / Financial
1. Trial Balance (period, YTD, comparative prior year, by cost centre/department, detailed & summary)
2. Trial Balance — Multi-currency (base and transaction currency columns)
3. Nominal Activity / Account Ledger Detail (opening, movements, closing, with drill-down)
4. Nominal Transaction Listing / Day Book (by date, batch, user, source module)
5. Journal Listing & Journal Audit (posted, unposted/deferred, reversing, recurring)
6. **Statement of Financial Position (Balance Sheet)** — current vs prior, with note references
7. **Statement of Profit or Loss and Other Comprehensive Income** — by nature and by function
8. **Statement of Changes in Equity**
9. **Statement of Cash Flows** — indirect and direct methods
10. Notes-to-the-accounts supporting schedules (per note, driven by account grouping)
11. Management Accounts Pack (P&L by cost centre/department, with budget, variance, % variance, YTD and full-year forecast)
12. Budget vs Actual vs Commitment (by account, cost centre, department, project)
13. Departmental / Cost Centre P&L and contribution analysis
14. Consolidated financial statements with elimination schedule
15. Foreign Exchange Revaluation Report (unrealised, by account/trader, before/after, rate used)
16. Realised FX Gain/Loss Analysis
17. Suspense & Unallocated Account Review
18. Control Account Reconciliation (AR/AP/Stock/VAT/Bank sub-ledger vs GL, with variance drill-down)
19. Period Close Checklist & Status report
20. Year-End Pack (TB before/after closing journals, closing entries listing, retained earnings movement)
21. Chart of Accounts Listing (with hierarchy, types, control flags)
22. Recurring Journals Due / Missed
23. Accruals & Prepayments Schedule with release profile
24. Intercompany Balances & Reconciliation
25. Hyperinflation Restatement Working Papers (optional module: index, factors, monetary gain/loss)

### A-B. Budgeting, Planning & Commitment Control
*(`B` prefix — separately licensable as module `BUD`. Common parameters add: Fiscal Year(s), Budget Type/Scenario, Version, Preparing Department, Hierarchy Level, Fund/Vote.)*

**Preparation & submission**
- B1. Budget Preparation Status Board (every department: not started / in progress / submitted / returned / recommended / approved, with deadline and days outstanding)
- B2. **Departmental Budget Submission** (one department's full submission, by account and period, with narrative and assumptions) — the document the budget holder signs
- B3. Target vs Submission Gap Report (ceiling issued vs amount requested, by department and category, with the unreconciled gap highlighted)
- B4. Consolidated Draft Budget by hierarchy level (cost centre → department → division → directorate → company)
- B5. Assumption & Driver Register (inflation, exchange rate, increment %, headcount, enrolment, volumes) with the lines each assumption feeds
- B6. **Assumption Sensitivity / What-If Impact** (change one driver, see the effect on every dependent line and on the bottom line)
- B7. Budget Preparation Basis Report (which lines were zero-based, incremental, actuals-based or driver-based)
- B8. Establishment-Driven Staff Budget (posts × grade × notch × scale + contributions + increments − vacancy factor, reconciled to the payroll budget line)
- B9. Capital Budget Submission & Phasing (project, year, funding source, S-curve phasing)
- B10. Revenue Budget Build-up (rates, service charges, fees, sales, stand sales — volume × rate, by source)

**Approval, versions and history**
- B11. **Budget Version Register** (every version ever created: number, type, status, author, date, reason, approval reference, sealed hash)
- B12. **Budget Version Comparison** (any two versions, any two years, any two scenarios — line by line, movement and %, with who-changed-what drill-down)
- B13. **Original vs Recommended vs Approved** (what the department asked for, what Finance proposed, what was approved — the cuts trail)
- B14. Budget Approval Audit (rounds, reviewers, returns, comments, committee minute references, turnaround days)
- B15. **Multi-Year Budget Comparison** (this year and N prior years: original, revised and actual for each, side by side)
- B16. **Archived Budget Retrieval Report** (produce any sealed prior-year budget in full, exactly as approved, with its integrity hash verified)
- B17. Superseded Versions Report (what replaced what, when and on whose authority)
- B18. Budget Line Change Log (old value, new value, user, timestamp, reason — within a draft version's life)

**Appropriation, virements and control**
- B19. **Approved Budget Register by Vote / Fund / Department** (the appropriation of record)
- B20. **Virement Register** (all transfers: source, target, amount, justification, approver, date)
- B21. **Budget Reconciliation: Original + Supplementary ± Virements ± Rephasing = Current Approved** (per line — the control report that proves the current budget is legitimate)
- B22. Supplementary Appropriation Schedule (additions and reductions, separately approved and separately reported)
- B23. Rephasing Report (value moved between periods within the year, distinct from virements)
- B24. **Vote Book** (per vote: appropriation, expenditure to date, commitments, reservations, unexpended balance) — councils will live in this report
- B25. **Commitment / Encumbrance Register** (open POs and requisitions in flight against each budget line)
- B26. Commitment Ledger Reconciliation to Open Purchase Orders (nightly exception report)
- B27. Budget Block & Override Report (transactions blocked for insufficient budget; overrides granted, by whom, with the stated reason)
- B28. Expenditure Against Unappropriated Vote (must be empty — any row is a governance incident)
- B29. Year-End Commitment Carry-Forward Schedule (lapsed vs carried, with approval reference)
- B30. Unspent Capital Carry-Forward Schedule

**Monitoring, variance and forecast**
- B31. **Budget vs Actual vs Commitment vs Available** — at any hierarchy level, period, YTD or full year, with drill-down to transactions
- B32. **Budget Holder Statement** (one manager's lines, spend, commitments, available balance, utilisation %) — emailed monthly
- B33. Budget Utilisation Report (% consumed by line and department, with a RAG status and projected full-year outturn)
- B34. **Over-Budget & Approaching-Threshold Exceptions** (lines past 90% / 100%, ranked by value at risk)
- B35. Variance Analysis with Decomposition (total variance split into volume, price/rate and FX components)
- B36. **Variance Commentary Pack** (material variances with the budget holder's written explanation) — attaches to the management accounts
- B37. Departmental Management Accounts (P&L by department: actual, budget, variance, YTD, prior year, full-year forecast)
- B38. **Full-Year Forecast / Outturn Projection** (actuals to date + forecast remaining = projected outturn vs approved budget)
- B39. Forecast vs Forecast Movement (what changed between reforecast rounds and why)
- B40. Rolling Forecast Report (N periods forward, refreshed each close)
- B41. Cash Budget vs Actual Cash Flow
- B42. Capital Project Budget vs Actual vs Commitment (by project, with % complete and cost-to-complete)
- B43. Revenue Budget Performance (billed and collected vs budgeted, by source — pairs with municipal collection efficiency)
- B44. Budget Accuracy Review (prior years: budget vs actual variance by department — who forecasts well and who does not)
- B45. Budget Audit Trail Report (all changes to budgets, versions, virements, targets and enforcement policy, with before/after values)

### B. Accounts Receivable / Customers
26. **Customer Statement — Open Item** (all outstanding documents, ageing summary box, remittance tear-off, multi-currency)
27. **Customer Statement — Balance Forward** (opening balance, period transactions, closing balance)
28. Customer Statement — Consolidated for group/parent accounts
29. Aged Debtors Summary & Detail (30/60/90/120+, configurable buckets, by due date or invoice date, as at any historical date)
30. Aged Debtors by Sales Agent / Branch / Customer Type / Currency
31. Customer Ledger / Account Enquiry Print
32. Customer Balances Listing (with credit limit, exposure, % utilised, over-limit flag)
33. Sales Day Book / Invoice Listing (by date, customer, agent, item, branch)
34. Sales Analysis: by customer, item, category, agent, branch, period, with margin and % of total
35. Top/Bottom N Customers by revenue, margin, growth, and by lapsed activity
36. Customer Sales History & Trend (12-month rolling, YoY comparison)
37. Credit Notes & Returns Analysis (with reason codes)
38. Receipts / Collections Report (by day, method, currency, user, till)
39. Allocations & Unallocated Receipts Report
40. Payment Performance / Days Sales Outstanding (DSO) by customer and overall
41. Promise-to-Pay Diary & Broken Promises
42. Dunning / Reminder Letters — Levels 1..n (as document layouts)
43. Bad Debt Provision (IFRS 9 ECL matrix) & Write-off Listing
44. Customer Deposits & Advance Payments Held
45. Lay-bye / Instalment Plan Status & Arrears
46. Sales Agent Commission Statement & Accrual Schedule
47. Debtor Exposure by Currency (with FX sensitivity)
48. New Customers / Dormant Customers Report
49. Customer Price List / Contract Price Report
50. Blank/Proforma: Sales Quotation, Sales Order Confirmation, Delivery Note, Invoice, Credit Note, Receipt, Remittance Slip

### C. Accounts Payable / Suppliers
51. **Supplier Statement / Remittance Statement** (our view of their account, for confirmation)
52. Aged Creditors Summary & Detail (as at any date, by currency, by category)
53. Supplier Ledger / Account Enquiry Print
54. Purchase Day Book / Supplier Invoice Listing
55. Purchase Analysis by supplier, item, category, cost centre, project
56. **Cash Requirement Forecast / Payment Due Report** (by week, by currency)
57. Payment Run Proposal, Payment Run Listing, and **Remittance Advice** (per supplier)
58. Payments Made Report (by method, currency, bank account, user)
59. **GRNI (Goods Received Not Invoiced) Report** — aged, with drill to GRN
60. Invoice Matching Exceptions (price/qty variance, unmatched, held/disputed)
61. Supplier Statement Reconciliation Working Paper
62. Withholding Tax Deducted Report + WHT Certificates
63. Supplier Performance Scorecard (on-time delivery %, quality rejects, price variance, lead time)
64. Supplier Balances Listing with prepayments and debit balances highlighted
65. Supplier Bank Detail Change Audit Report (fraud control)
66. Top N Suppliers by spend / spend concentration analysis
67. Blank/Proforma: Purchase Requisition, Purchase Order, GRN, Debit Note, Payment Voucher, Cheque

### D. Cash Book / Treasury
68. Cash Book Listing (receipts & payments by bank account, date, currency)
69. **Bank Reconciliation Statement** (balance per bank, add/less unpresented, per cash book)
70. Unpresented Cheques / Uncleared Deposits Ageing
71. Bank Account Balances Summary (all accounts, all currencies, base equivalent)
72. Cash Flow Forecast (13-week rolling, by currency)
73. Petty Cash Book & Imprest Reconciliation
74. Inter-account Transfers Report (with FX differences)
75. Cheque Register (issued, cancelled, voided, stale)
76. Mobile Money / Wallet Reconciliation

### E. Inventory / Stock
77. **Stock Valuation Report** (as at any date, by warehouse, category, costing method, with unit cost and total)
78. Stock Valuation Movement / Reconciliation to GL Stock Control
79. Stock on Hand / Availability (on hand, allocated, on order, free stock, per warehouse & bin)
80. Stock Movement / Transaction History (by item, date, type, with running balance and cost)
81. Stock Take / Count Sheets (blind and pre-printed) and **Stock Take Variance Report** (qty and value, with proposed adjustment journal)
82. Stock Adjustments & Write-offs by reason code and user
83. Slow-Moving / Obsolete / No-Movement Stock (configurable ageing)
84. Stock Ageing by receipt date / batch expiry
85. **Reorder / Replenishment Suggestion** (below reorder level, with suggested order qty and preferred supplier)
86. Overstock / Excess Inventory
87. Negative Stock Report (must be empty — exception monitoring)
88. Batch / Lot Traceability — forward (batch → customers) and backward (sale → supplier)
89. Serial Number Register & History
90. Expiry / Shelf-life Report (expiring in N days, expired)
91. Stock Transfers In-Transit Report
92. Inventory Turnover & Days Cover by item/category
93. Gross Margin by Item / Category / Department (with cost basis disclosed)
94. Price List / Shelf Label / Bin Label print (with barcode)
95. Item Master Listing & Item Attribute/Variant matrix
96. Cost Change History / Price Change Audit
97. Landed Cost Analysis by shipment
98. Kit / BOM Explosion & Assembly Cost Sheet
99. Yield / Disassembly Report (butchery: carcass in vs cuts out, yield %, cost per cut)
100. Wastage / Shrinkage Report (by department, with % of sales)
101. Consignment Stock Report

### F. POS / Retail Operations
102. **Z-Read (End of Shift)** and **X-Read (Mid-shift)** — by till, cashier, shift, with tender breakdown per currency
103. Daily Sales Summary (by department, category, hour, till, cashier, tender type)
104. Cashier Performance (sales, items/hour, avg basket, voids, refunds, discounts, overrides, variances)
105. **Till Variance / Cash-up Report** (expected vs counted per currency and denomination)
106. Voids & Cancelled Sales Report (with reason and authorising supervisor)
107. **Refunds & Returns Report** (with reason, original receipt link, no-receipt refunds flagged)
108. Discounts Given Report (by type, cashier, customer, item, with authorisation audit)
109. Price Override Report
110. No-Sale / Drawer Open Report
111. Suspended/Parked Sales Not Recalled
112. Hourly Sales Heatmap & Basket Analysis (avg basket value, items per basket, peak times)
113. Product Mix / Sales Mix Ranking (top sellers, non-sellers)
114. Promotion Effectiveness (units, revenue, discount cost, margin impact, uplift vs baseline)
115. Loyalty Report (points issued, redeemed, expired, liability, active members)
116. Receipt Reprint Log
117. Tender Type Summary & Multi-currency Tender Analysis (with FX gain on cash)
118. Table / Tab Report for bars (open tabs, average time, per waiter, tips)
119. Kitchen/Bar Prep Ticket volumes and void-after-print report
120. Offline Transaction Sync Log & Exceptions
121. Fiscal Day / Fiscal Device Submission Report (submitted, queued, failed)
122. Receipt / Invoice / Refund Slip layouts (58 mm, 80 mm, A4)

### G. Procurement
123. Requisition Listing by status / requester / department / age
124. **Requisition Approval Status & Turnaround** (time at each stage, pending-with-whom, SLA breaches)
125. Approval History / Authorisation Audit Report (who approved what, value, date, delegation used)
126. Purchase Order Listing (open, part-received, closed, cancelled, by supplier/buyer/project)
127. **Outstanding Purchase Orders / Order Book** with expected delivery dates and overdue flags
128. Committed Cost / Encumbrance Report by budget line
129. Goods Received Listing & Receipt vs Order Variance
130. Rejected Goods / Returns to Supplier
131. RFQ / Quotation Comparison & Award Adjudication Report
132. Purchase Price Variance Analysis
133. Contract/Framework Agreement Utilisation
134. Procurement Spend Analysis (by category, supplier, department, month; spend under management)
135. Lead Time Analysis (order to receipt)
136. Budget Availability Report at requisition point

### H. Sales Order Processing / Distribution
137. Order Book / Open Sales Orders with allocation status
138. Back Order Report
139. Picking Lists, Packing Notes, Despatch Notes
140. Despatch / Delivery Performance (on-time in-full %)
141. Proof of Delivery Outstanding
142. Delivery Schedule / Load Sheet by vehicle and route
143. Quotation Listing & Conversion Rate

### I. Municipality / Council
144. **Consumer / Ratepayer Statement** (property, services, opening balance, charges, payments, interest, closing balance, ageing) — printable per account and **burst in bulk**
145. **Rates & Services Bill / Invoice** (the monthly bill layout, with consumption graph, meter readings, tariff breakdown, messages)
146. Billing Run Summary & Control Report (accounts billed, total by service, by ward, compared to prior period, exceptions)
147. Billing Exceptions (no reading, estimated, zero consumption, abnormal high/low, unbilled services, inactive meters)
148. **Aged Debtors by Ward / Zone / Consumer Category / Service Type**
149. Arrears Report with last payment date and arrangement status
150. Revenue Collection Report (billed vs collected, collection efficiency %, by service, by ward, by month)
151. **Valuation Roll Report** (properties, valuations, category, effective date) and Supplementary Roll
152. Property Register Listing (with owner, services, meters, status)
153. Ownership Transfer / Clearance Certificate Register + **Rates Clearance Certificate** document
154. **Meter Reading Sheets / Route Books** (pre-printed, per route, with previous readings)
155. Meter Reading Import Exceptions & Validation Report
156. Consumption Analysis (per property, per category, per ward; trend and average)
157. **Water Balance / Non-Revenue Water Report** (bulk supply vs billed consumption, losses %)
158. Unread / Faulty / Tampered Meter Report
159. Meter Register & Meter Replacement History
160. Disconnection List / Reconnection List with fees
161. Payment Arrangements Report (active, honoured, defaulted)
162. Indigent / Social Tariff Register & Subsidy Cost Report
163. Interest & Penalties Charged Report
164. Write-offs & Adjustments Report (with approval reference)
165. Deposits Held by Consumer
166. Licences & Permits Register, Renewals Due, Expired Licences, Licence Fee Collection
167. Service Request / Fault Ticket Report (volumes, SLA performance, backlog by type and ward)
168. Tariff Schedule Report (current and historical, with effective dates)
169. Revenue by Source / Vote Report (for council reporting and treasury submissions)
170. Ward Councillor Pack (summary of billing, collection, arrears, faults for one ward)
171. Debtors Age Analysis for statutory/treasury returns (format-configurable)
172. Prepaid vs Conventional Consumer Analysis

### I-B. Housing, Land & Estates
*(Numbered with an `H` prefix so this section can be licensed, shipped and versioned independently of the municipal set. Common parameters add: Scheme/Project, Ward/Suburb, Stand Status, Tenure Type, Waiting List Category, Allocation Date range.)*

**Land bank & acquisition**
- H1. **Land Bank Register** (all parcels: reference, locality, area, tenure, zoning, occupation status, acquisition date, cost, current status) — the report the director asks for first
- H2. Land Acquisition Register (by method: purchase / donation / state grant / compulsory / swap, with council resolution reference and price)
- H3. Land Acquisition Cost Build-up per parcel (price, survey, legal, valuation, compensation, duties → total and cost per m²)
- H4. Acquisitions in Progress / Due Diligence Outstanding (which checklist items are blocking which parcel)
- H5. Compulsory Acquisition Compensation Register (notices served, objections, assessed, offered, paid, outstanding)
- H6. Land Disposals & Exchanges Register
- H7. Encumbrances, Servitudes & Caveats on Land Report
- H8. Disputed & Encroached Land Report (with dispute age and current legal stage)
- H9. Land Bank Valuation vs Carrying Value (IAS 2 NRV test working paper)

**Planning, survey & servicing**
- H10. Layout Plans Register & Approval Status (submitted, approved, amended, rejected, with turnaround days)
- H11. **Stand Creation / Subdivision Report** (parent parcel → stands created, by use and density, with lineage)
- H12. Survey Status Report (pegged / diagram issued / awaiting surveyor-general, by scheme, with age at each stage)
- H13. **Servicing Status by Scheme** (water, sewer, roads, electricity, storm water — % complete, stands serviced vs unserviced)
- H14. Servicing Cost Apportionment Schedule (actual servicing spend → cost per stand, showing the apportionment basis used)
- H15. **Cost per Stand / Selling Price Build-up** (land + servicing + overhead + levy → price, with margin and cost-recovery %)
- H16. Price List by Scheme & Stand Size Band, with price change history and approval references
- H17. Stand Inventory Movement & Reconciliation (opening stands + created + repossessed − sold − consolidated = closing, in units and in value — must reconcile to the GL inventory control account)

**Waiting list**
- H18. **Waiting List Register** (by category, in seniority order, with registration number, date, score and position)
- H19. Waiting List Summary by Category, Ward, Income Band, Gender, Age Band and Quota Group
- H20. **Waiting List Age Analysis** (how long applicants have been waiting: <1yr, 1–3, 3–5, 5–10, 10+ years)
- H21. Applicant Detail / Verification Sheet (single applicant, all captured data, documents held, score derivation) — printable for the applicant
- H22. **Priority Score Derivation Report** (how each applicant's points were computed, factor by factor)
- H23. **Duplicate & Suspected Fraudulent Applicants** (fuzzy name/ID/DOB matches across list, allocations and property register)
- H24. Applicants Already Owning Property (auto-disqualification candidates, cross-checked against stand register and rates roll)
- H25. Registration Renewal Compliance (confirmed, not confirmed, suspended, removed — with proof-of-contact history)
- H26. **Registration Date Change Audit** (every change to a seniority date, who, when, authorised by whom, reason) — a top-three fraud control
- H27. Registration Fee Collection Report
- H28. New Registrations & Removals Movement (period-on-period)
- H29. Demand vs Supply Report (applicants by category vs stands available by category — the housing gap)

**Allocation**
- H30. **Proposed Allocation List** (pre-approval working document: stand, applicant, seniority/score, quota group)
- H31. **Allocation Register** (approved allocations: stand, beneficiary, date, price, committee minute reference, approving officers)
- H32. Ballot / Draw Result Report (with the stored seed and input-list hash for independent re-verification)
- H33. Allocations by Officer, by Scheme, by Month (workload and concentration analysis)
- H34. **Skipped Applicant / Substitution Report** (who was passed over on each allocation, and the stated reason) — the appeal-defence report
- H35. Offers Outstanding / Expiring / Expired (with acceptance deadline countdown)
- H36. Declined, Lapsed & Forfeited Offers (with deposit forfeiture amounts)
- H37. Quota Compliance Report (actual allocations by quota group vs policy percentage, per scheme)
- H38. Appeals Register & Outcomes
- H39. **Double Allocation Exception Report** (must always be empty — any row is an incident)
- H40. Beneficiaries with More Than One Stand (across schemes, including spouse ID matching)
- H41. Allocation-to-Agreement Conversion Report (allocated but no agreement signed, with age)

**Agreements, instalments & beneficiary accounts**
- H42. Agreement Register by tenure type, scheme and status (draft / signed / active / settled / cancelled)
- H43. **Beneficiary Account Statement** (all charges, instalments, receipts, interest, penalties, balance, next due) — printable per beneficiary and **burstable in bulk**
- H44. **Instalment Amortisation Schedule** (capital, interest, balance per instalment) — issued to the beneficiary at signature
- H45. **Housing Debtors Age Analysis** (by scheme, tenure type, ward, officer; 30/60/90/120+; as at any historical date)
- H46. Instalment Arrears & Default Report with last payment date and arrangement status
- H47. Payment Arrangements Register (active, honoured, defaulted)
- H48. Collections Report (by scheme, method, currency, day, officer) and Collection Efficiency %
- H49. **Fully Paid / Ready for Transfer Report** (beneficiaries who have settled but have no title — the most actionable list in the department)
- H50. Early Settlement & Rebate Report
- H51. Deposits Held Register (liability listing, by scheme and beneficiary)
- H52. **Expected Credit Loss (IFRS 9) Provision Working** for housing debtors, by arrears bucket and scheme
- H53. Cancellations & Refund Computation Report (paid less occupation rental, admin charge and arrears)
- H54. **Development Covenant Compliance** (stands not developed within the deadline, by scheme, with extension and breach status)
- H55. Repossession Pipeline & Register (notice issued, committee approved, repossessed, re-allocated) with financial effect

**Change of ownership, cession & title**
- H56. **Ownership History / Chain of Title per Stand** (every holder, from–to dates, transfer type, consideration, authority, document references) — the single most important printout the department issues
- H57. **Change of Ownership Register** (all transfers in a period, by type: sale, cession, inheritance, donation, court order, name change, correction)
- H58. Transfers in Progress / Pending Approval (with age at each stage and who is holding it up)
- H59. **Transfers Blocked by Outstanding Rates or Housing Arrears** (with the amount required to clear)
- H60. Transfer Checklist Exception Report (which mandatory documents are missing per application)
- H61. Deceased Estate Transfers Register (with executor, letters of administration reference, and beneficiaries)
- H62. Transfer, Cession & Administration Fee Collection Report
- H63. Withholding / Transfer Duty Deducted and Remitted on Transfers
- H64. **Title Deed Pipeline Report** (applied → lodged → queried → registered → received → handed over, with days at each stage and conveyancer)
- H65. **Title Deeds Held but Uncollected** (aged, with owner contact details) — chase list
- H66. Deeds Register (deed numbers, registration dates, current custody)
- H67. Conveyancer Performance (files allocated, registered, average days, queries raised)
- H68. **Caveats, Blocks & Disputes Register** (placed by, reason, date, supporting document, lift authority, age)
- H69. Stands with No Recorded Owner / Broken Ownership Chain (data-integrity exception — must be empty)
- H70. Sectional Title Unit Register & Participation Quotas

**Council rental housing stock**
- H71. **Rent Roll** (all units, tenant, rent, arrears, lease end) by suburb and unit type
- H72. Housing Stock Register & Condition Report (units, type, rooms, condition grade, last inspection)
- H73. **Occupancy & Voids Report** (occupied, void, days void, reason, rent lost to voids, re-let performance)
- H74. Tenant Statement (charges, receipts, balance) — burstable
- H75. **Rent Arrears Age Analysis** by suburb, unit type and officer
- H76. Eviction Pipeline & Register (stage, dates, legal reference, outcome)
- H77. Lease Expiry & Renewal Due Report
- H78. Tenant Deposits Held & Refunds Due
- H79. Maintenance & Repairs Cost per Unit (with recharges to tenants separated)
- H80. Inspections Due & Findings Summary
- H81. Straight-lined Rental Income Schedule (IFRS 16 lessor working paper)

**Schemes, partners, cooperatives & settlements**
- H82. **Scheme Delivery Dashboard** (stands planned / surveyed / serviced / allocated / sold / titled, % complete, against milestone dates)
- H83. **Scheme Profitability & Cost Recovery** (total cost in, revenue recognised, margin, cost recovered %, per stand)
- H84. Scheme Budget vs Actual (land, servicing, professional fees, overheads)
- H85. Developer / Joint Venture Reconciliation (stands contracted vs delivered, council quota vs developer quota, penalties)
- H86. Housing Cooperative Register, Membership and Cooperative Account Statement
- H87. Informal Settlement / Enumeration Register (settlements, structures, households, eligibility status)
- H88. Regularisation Progress Report (enumerated → qualified → allocated → agreement → title)
- H89. Housing Subsidy & Grant Utilisation (IAS 20 — received, applied, deferred, conditions outstanding)

**Control, audit & executive**
- H90. **Housing Audit Trail Report** (all changes to stands, allocations, prices, ownership, registration dates, caveats — before/after values, user, authority)
- H91. Allocation & Ownership Change Approval Audit (who approved what, value, turnaround, delegations used, dual-authorisation exceptions)
- H92. **Segregation of Duties Violations — Housing** (allocation vs approval, clearance verification vs transfer approval, caveat place vs lift)
- H93. Manual Override & Exception Report (price overrides, waived clearances, forced status transitions — each with the stated reason)
- H94. Housing Revenue Summary (stand sales, instalment interest, rentals, levies, transfer fees, forfeitures) vs budget
- H95. **Housing Executive Dashboard** (land bank hectares and stands available, waiting list size and ageing, allocations and titles issued YTD, debtors and collection efficiency, rental occupancy, deeds pipeline age)
- H96. Housing Delivery / Statutory Return (units and stands delivered by density, tenure and beneficiary category — format-configurable for national reporting)
- H97. Ward Housing Pack (one ward: stands, allocations, beneficiaries, arrears, rental units, waiting list) for councillors

### J. Schools
173. **Student Fee Statement** (per student and per guardian, with term detail and arrears)
174. Fee Invoice / Fee Notice per term (burstable to guardians)
175. Fee Billing Run Summary (by class, fee item, boarding status)
176. **Arrears by Class / Form / Stream** with contact details
177. Collection Report by day/term/payment method
178. Bursary, Scholarship & Discount Report (cost of discounts granted)
179. Sponsor Account Statement (one sponsor, many students)
180. Enrolment & Headcount Report (by class, gender, boarding, term-on-term movement)
181. Deferred Fee Income Schedule (billed vs recognised)
182. Student Balances Listing (debit and credit balances, leavers with balances)
183. Transport Route Billing & Utilisation
184. Boarding / Hostel Occupancy & Billing
185. Tuck Shop Sales & Student Account Spend

### K. Construction / Job Costing
186. **Job Cost Report** (budget vs committed vs actual vs variance, by cost head, with drill-down to transaction)
187. Job Profitability / Contract Summary (revenue recognised, cost, margin, % complete)
188. **Interim Payment Certificate / Progress Valuation** document
189. Cost to Complete / Forecast Final Cost
190. **WIP & Retention Report** (retention held, due for release, defects liability expiry)
191. Variation Order Register (submitted, approved, rejected, value impact)
192. Subcontractor Ledger, Certificates & Retention Report
193. Labour & Timesheet Cost Allocation by job
194. Plant & Equipment Utilisation, Hire Recovery and Running Cost per unit
195. Materials Issued to Job / Site Stock Report
196. BOQ vs Actual Quantities (measured work)
197. Contract Cash Flow (certified vs received vs costs paid)

### L. Weighbridge / Bulk Materials
198. **Weighbridge Ticket** (gross/tare/net, vehicle, product, customer, driver, time in/out, signatures)
199. Daily Weighbridge Register & Tonnage Summary (by product, customer, vehicle, driver)
200. Vehicle Movement & Turnaround Time Report
201. Deliveries by Customer / Site with haulage charges
202. Manual Weight Override Report (control exception)
203. Product Yield / Stockpile Reconciliation (opening + produced + received − sold = closing)
204. Concrete Batch Report (mix design, materials consumed, variance to recipe, m³ delivered)
205. Haulage / Transport Charge Analysis by zone and distance

### M. Fixed Assets
206. Fixed Asset Register (full and summary, by class, location, custodian)
207. **Depreciation Schedule** (per period and annual, by class and asset)
208. Asset Additions, Disposals & Transfers Report (with profit/loss on disposal)
209. Asset Movement Schedule (the note to the accounts: cost and accumulated depreciation roll-forward)
210. Asset Verification / Count Variance Report
211. Capital Work in Progress Report
212. Assets Fully Depreciated Still in Use
213. Insurance & Warranty Expiry Report
214. Maintenance Schedule & History

### N. Tax & Statutory
215. **VAT/Sales Tax Return** (box-mapped, with drill-down to transactions)
216. VAT Detail / Audit Report (all transactions by tax code, input and output)
217. VAT Reconciliation (return vs GL control accounts vs revenue)
218. Exempt / Zero-rated / Out-of-scope Analysis
219. Reverse Charge & Import VAT Report
220. Withholding Tax Return & Certificates
221. PAYE / Payroll Tax Returns (if payroll module licensed)
222. Statutory Deduction Schedules
223. Tax Invoice / Fiscal Receipt compliance layouts
224. Annual Financial Statements Pack (IFRS presentation, with comparatives and notes)
225. Audit File Export (SAF-T style: GL, AR, AP, inventory, master data — configurable format)

### O. Payroll & HR
*(`PR` prefix — separately licensable as module `PAY`. **Every report in this section is permission-gated and every print/export is logged**, because a payroll report is a data-breach risk. Common parameters add: Payroll/Pay Group, Pay Period, Currency, Department/Cost Centre, Employee Status.)*

**The run itself**
- PR1. **Payslip** (per employee, multi-currency, with earnings, deductions, employer contributions, taxable benefits as memo, YTD totals, leave and loan balances) — printable, bulk-burstable, emailable as a password-protected PDF
- PR2. **Payroll Summary** (totals by earning, deduction, contribution and net pay, by pay group and department) — the one-page the officer checks before approving
- PR3. **Payroll Journal** (the GL posting by account, department, cost centre, project and fund, with the double entry shown)
- PR4. **Pre-Commit Validation / Exception Report** — negative net pay, zero net pay, net pay varying >N% from last period, missing bank details, **duplicate bank accounts across employees**, duplicate national IDs, missing tax numbers, terminated employees still in the run, employees missing since last run, negative leave, deductions suspended by net-pay protection, salary outside grade band, expired contracts, **bank details changed since the last run**
- PR5. **Payroll Variance / Movement Report** (this period vs last, per employee and per element, with every difference explained — the report that catches the mistake before it reaches the bank)
- PR6. **Per-Employee Calculation Trace** (every element, its formula, its inputs and its result) — how the officer answers "why is my tax higher this month" without opening a spreadsheet
- PR7. Payroll Run Control Report (run status, who calculated, who approved, who committed, timestamps, totals at each stage)
- PR8. **Parallel Run Comparison** (new system vs legacy, line by line, per employee and per element) — mandatory at go-live
- PR9. Adjustment / Supplementary Run Report and Payroll Reversal Register
- PR10. Backpay & Retrospective Adjustment Report

**Payment**
- PR11. **Bank EFT Schedule** per bank, per currency, with control totals and file hash
- PR12. **Cash Payroll Denomination Breakdown** per pay point and per currency
- PR13. Mobile Money / Wallet Payment Schedule
- PR14. Split-Pay Report (employees paid across multiple accounts or methods)
- PR15. Unpaid / Held Salaries Report (with reason and release authority)
- PR16. **Net Pay Control Account Reconciliation** (payroll net pay vs bank payments — must clear to zero)

**Earnings, deductions and third parties**
- PR17. Earnings Analysis by element, department and period (with period-on-period trend)
- PR18. Deductions Analysis by element, with employee counts and totals
- PR19. **Third-Party Remittance Schedules** — tax authority, pension fund, medical aid, union, savings scheme, funeral scheme, garnishee creditors (each in the recipient's required format)
- PR20. Third-Party Deduction vs Remittance Reconciliation (deducted, remitted, outstanding — per scheme, per period)
- PR21. **Garnishee & Court Order Register** (order reference, priority, amount, balance, protected-earnings effect)
- PR22. **Loan & Advance Register and Balances** (issued, repaid, outstanding, suspended, deemed-benefit taxable value)
- PR23. Staff Account / POS Staff Purchase Deductions Reconciliation
- PR24. Employer Housing Rent Deduction Reconciliation (links to §4.10.7)
- PR25. Overtime Analysis (hours and cost by department, employee and multiplier, vs approved overtime)
- PR26. Allowance Analysis (acting, responsibility, shift, standby — with eligibility review)

**Tax and statutory**
- PR27. **Monthly Tax (PAYE) Remittance Return** — format-configurable, per currency
- PR28. **Annual Employee Tax Certificates** (per employee) and the consolidated annual reconciliation
- PR29. **Tax Reconciliation** (tax per payroll vs tax per GL vs tax remitted, per period and YTD)
- PR30. Social Security / National Insurance Schedule and Return
- PR31. Pension Fund Contribution Schedule (employee, employer, per member number)
- PR32. Medical Aid Schedule (member, dependants, employee and employer split)
- PR33. Workers' Compensation Return and Skills/Manpower Levy Return
- PR34. **Taxable Benefits Schedule** (motor vehicle, housing, loan, school fees — value, basis and tax effect per employee)
- PR35. **Multi-Currency Tax Split Report** (earnings, bands applied, tax computed and remitted per currency) — the working paper for a split-currency payroll assessment
- PR36. Tax Table in Force Report (bands, rates, credits, effective dates — proves which table a prior period used)

**Leave and attendance**
- PR37. **Leave Balances** (entitlement, taken, accrued, carried forward, expiring, current balance) by employee and department
- PR38. **Leave Liability Provision (IAS 19)** — untaken leave valued at current rates, with the period movement and the journal
- PR39. Leave Taken Analysis by type, department and month; leave expiring within N days
- PR40. Absence & Lateness Report; Sick Leave Pattern Analysis (with full-pay/half-pay tier status)
- PR41. Leave Encashment Report
- PR42. Timesheet Hours Summary and **Labour Cost Allocated to Jobs** (reconciled to the payroll journal — hours costed must equal hours paid)
- PR43. Attendance Exception Report (no clock-in, unapproved overtime, hours exceeding statutory maxima)

**Employees, establishment and movements**
- PR44. Employee Master Listing (full and summary, by department, grade, contract type, status)
- PR45. **Headcount Report and Reconciliation** (opening + engagements − terminations = closing, by department, gender, grade and contract type)
- PR46. **Starters & Leavers Report** and Staff Turnover % (annualised, by department and reason)
- PR47. **Establishment vs Actual** (approved posts, filled, vacant, over-establishment, by grade and department) with the vacancy cost saving
- PR48. Salary Scale & Grade Distribution (employees per notch, compa-ratio, employees at scale maximum)
- PR49. **Salary Change / Increment Audit** (every salary change: old, new, %, effective date, authorised by whom)
- PR50. **Bank Detail Change Report** (every change, with the prior and new account and the approving officer) — the fraud control
- PR51. Contract Expiry, Probation Due and Work Permit Expiry (next 30/60/90 days)
- PR52. **Termination & Final Settlement Report** (notice, leave encashment, gratuity, severance, recoveries, tax on terminal benefits — per employee, showing the computation)
- PR53. Clearance Checklist Outstanding (leavers with assets, loans or staff accounts unsettled)
- PR54. Gratuity & Long Service Provision Schedule (IAS 19 other long-term benefits, discounted)
- PR55. Employee Documents Expiring / Missing (ID, contract, qualifications, medicals)
- PR56. Disciplinary & Warning Register (with warning expiry)
- PR57. Training & Qualification Register; Appraisal Due Report

**Cost analysis**
- PR58. **Total Cost to Company** per employee (gross + employer contributions + benefits + provisions)
- PR59. **Labour Cost by Department / Cost Centre / Project / Fund** (actual, with period trend)
- PR60. Payroll Cost vs Budget (against the establishment-driven staff budget from §4.18)
- PR61. Payroll as % of Revenue / of Operating Cost (trend)
- PR62. Overtime and Casual Labour Cost vs Permanent Establishment Cost
- PR63. Payroll Accrual Schedule (unpaid salaries at period end)

**Control and audit**
- PR64. **Payroll GL Reconciliation** (payroll totals vs GL postings vs statutory liability balances — each liability account must clear)
- PR65. **Payroll Audit Trail** (every change to employee master, salary, bank details, pay elements, tax tables and runs — before/after values, user, authority)
- PR66. **Segregation of Duties Violations — Payroll** (process + approve, master maintenance + approve, bank change + payment approval, **and any user who processed a run containing their own record**)
- PR67. Payroll Access & Report Print/Export Log (who viewed or exported payroll data, when, and what)
- PR68. Ghost Worker Detection Pack — duplicate national IDs, duplicate bank accounts, duplicate addresses, employees with no leave ever taken, employees never appraised, employees with no engagement documents, and employees whose bank account matches a supplier's
- PR69. Payslip Reprint Log
- PR70. YTD Earnings & Deductions Register (per employee, all elements, for the tax year)

### P. Audit, Security & Compliance
235. **Audit Trail Report** (filterable by user, date, module, entity, action, with before/after values)
236. User Activity Report (logins, failed logins, hours active, screens used)
237. **Permission Matrix Report** (users × permissions, roles × permissions, printable for auditors)
238. Role & Group Membership Report
239. **Segregation of Duties Violation Report**
240. Master Data Change Report (customers, suppliers, items, prices, tariffs, bank details, users)
241. Supervisor Override Report
242. Back-dated Transaction Report
243. Deleted/Voided Document Report
244. Document Numbering Gap Analysis (fiscal control)
245. Unposted / Deferred Batch Report
246. Data Integrity / System Health Report (orphans, out-of-balance batches, control variances)
247. Audit Trail Hash-Chain Verification Report
248. Licence & Module Usage Report
249. Login Session & Concurrency Report
250. Report Print/Export Log (who printed what, when — data-leak control)

### Q. Executive / BI Dashboards (on-screen, with print to PDF)
251. Executive Dashboard: revenue, gross margin, cash position by currency, debtors, creditors, stock value, top movers, with period comparisons
252. Daily Flash Report (yesterday's sales, collections, payments, cash balances) — emailable each morning
253. KPI Scorecard (configurable: DSO, DPO, stock turn, gross margin %, collection efficiency, till variance %)
254. Branch / Store Comparison League Table
255. Trend & Seasonality Analysis
256. Municipal Executive Dashboard (billing vs collection by ward, arrears trend, fault backlog, water losses)

### R. Master Data & System Listings
257. Customer / Supplier / Item / Property / Student / Employee master listings with all attributes
258. Price List & Tariff Listings
259. Currency & Exchange Rate History Report
260. Number Sequence Status
261. Company Configuration / Parameter Listing
262. Report Definition Inventory (which reports exist, custom vs system, last modified)
263. Scheduled Job Status & Delivery Log

### S. Document Layouts (print templates, all designer-editable)
Sales quotation, sales order, proforma invoice, tax invoice (A4 + thermal), credit note, debit note, delivery note, packing note, receipt, customer statement, dunning letters, remittance advice, purchase requisition, purchase order, GRN, supplier debit note, payment voucher, receipt voucher, journal voucher, petty cash voucher, cheque, EFT advice, stock transfer note, stock count sheet, shelf/bin/scale labels, weighbridge ticket, delivery/dispatch ticket, municipal bill, ratepayer statement, clearance certificate, disconnection notice, meter reading sheet, waiting-list registration certificate/card, ballot result notice, stand offer letter, allocation letter, agreement of sale, lease agreement, long-lease agreement, instalment schedule, beneficiary statement, consent to cede, cession/transfer agreement, transfer checklist, change-of-ownership certificate, chain-of-title printout, title deed handover receipt, demand letter, notice of breach, notice of cancellation, notice of intention to repossess, stand inspection form, tenancy agreement, notice to vacate, rent statement, payslip, employee tax certificate, bank EFT schedule, cash pay denomination sheet, certificate of service, final settlement statement, loan agreement and statement, leave application and leave balance certificate, employment contract, budget submission pack, budget-holder statement, virement request form, vote book page, supplementary appropriation schedule, school fee invoice, fee statement, student receipt, payment certificate (construction), subcontractor certificate, gift voucher, licence certificate, permit, WHT certificate, asset tag.

---

## 15. NON-FUNCTIONAL REQUIREMENTS
- POS: item lookup < 100 ms on 100k SKUs; sale completion (post + print) < 2 s; POS start-to-ready < 10 s.
- Back office: standard list screens paginate server-side, first page < 1 s on 1M rows.
- Billing run: 50,000 municipal accounts in < 15 minutes, resumable, with progress.
- Payroll: calculate 1,000 employees in < 60 s with a full per-element trace retained; payslip PDF generation and emailing for 1,000 employees in < 5 minutes.
- Budgeting: 40 departments preparing concurrently with no lock contention; budget-vs-actual-vs-commitment across 12 periods and 5,000 lines renders in < 3 s; a driver/assumption change re-computes and previews in < 10 s.
- Housing: allocation run over a 100,000-row waiting list produces a proposed list in < 60 s; duplicate-detection sweep across waiting list, allocations and property register completes nightly in < 10 minutes.
- Reports: parameterised report of 100k rows renders first page < 5 s, streams thereafter.
- Concurrency: 50 concurrent users / 20 POS terminals per server as a baseline target.
- Resilience: POS never blocks on network; all long jobs resumable; every batch job idempotent.
- Accessibility: keyboard navigation everywhere; scalable fonts; high-contrast theme for POS.
- Localisation-ready: all UI strings in resource files; date/number formats from company locale; multi-language report labels.

---

## 16. DELIVERY PLAN (build in this order)

**Phase 0 — Foundations (no business features)**
Solution skeleton, DI, config, logging, `Result<T>`, Money/Currency value objects, EF Core + Npgsql, migration runner, Testcontainers test harness, CI build, code style, ADR folder.

**Phase 1 — System Manager + Security + Licensing**
Companies, branches, users, roles, groups, **permission grid**, scopes, audit trail (both layers), periods, currencies & rates, number sequences, `Meridian.Licensing.Core` + `Generator`, licence enforcement in the shell. Ship the WPF shell with navigation driven by licence + permissions.

**Phase 2 — Accounting Core**
Chart of accounts, posting engine, journals, period control, year-end, tax engine, FX revaluation, control-account framework, **Trial Balance + Nominal Activity + Balance Sheet + P&L** as the first real reports. Accounting test suite green.

**Phase 3 — Reporting Engine + Designer**
Definition model, expression engine, engine + renderers (WPF preview, PDF, XLSX, CSV, ESC/POS), viewer control, designer UI, scheduling and **bursting**, report permissions, seeded system reports from Phase 2. *(Do this early — every later module ships its reports on this foundation.)*

**Phase 4 — AR + AP + Cash Book**
Traders, documents, allocation, ageing, statements (open item + balance forward), payment runs, bank reconciliation, dunning, and their full report sets.

**Phase 4B — Budgeting, Planning & Commitment Control**
Budget structure on the full posting dimension set, departmental preparation workspaces with the org hierarchy, top-down targets vs bottom-up submissions, driver/assumption register, spreading profiles, **immutable versioning with permanent retention and sealed approval snapshots**, workflow through to board/council approval, supplementary budgets and virements with the reconciliation control report, and budget-vs-actual reporting. Commitment control is wired in during Phase 5 when procurement exists — build the available-balance service now and have Procurement call it at all four gates. Module code `BUD`.

**Phase 5 — Inventory + Procurement + Workflow engine**
Items, warehouses, costing, movements, stock take, batch/serial; requisition → RFQ → PO → GRN → 3-way match; the generic approval workflow; commitment accounting; report sets.

**Phase 6 — POS**
Full-screen POS, search/filters/numberpad, discounts, refunds, void/recall, split bill, tabs/tables, multi-currency tendering, promotions, loyalty, vouchers, till sessions, X/Z reads, offline mode + sync, device layer, fiscal hook, POS report set.

**Phase 7 — Voucher Management + SOP/Distribution**

**Phase 8 — Municipality module** (properties, valuation roll, services, meters, tariffs, billing runs, arrears, debt management, clearance certificates, service tickets, full municipal report set)

**Phase 8B — Housing, Land & Estates module**
Land bank and acquisition, subdivision into stands with lineage, servicing cost apportionment and price build-up, waiting list with qualification rules and duplicate detection, allocation with ballot and dual authorisation, agreements and instalment schedules, **change of ownership / cession with the clearance gate and the append-only ownership chain**, title deed pipeline, council rental stock, and the full `H`-series report set. Separately licensable (module code `HSE`) — build it so it runs with or without the Municipality module, degrading the clearance check to a manual, audited attestation when `MUNI` is not licensed.

**Phase 9 — Schools module**

**Phase 10 — Construction / Job Costing + Weighbridge & bulk materials**

**Phase 11 — Fixed Assets + CRM/Service Desk**

**Phase 11B — HR & Payroll**
Employee master, establishment and salary scales, pay element engine with the expression evaluator, effective-dated tax tables and multi-currency tax split, leave with IAS 19 liability, loans and garnishees, the full run state machine with the pre-commit exception report and maker-checker, immutable committed runs, bank files and password-protected payslips, third-party remittances into AP, terminations and final settlements, the `PR`-series reports, and the walled-garden security model (own schema, encrypted columns, own audit visibility, self-payslip block). Module code `PAY`. **Insist on a parallel run (PR8) before any client goes live.** *(The employee master, establishment and timesheets must ship earlier, in Phase 5, because job costing and the establishment-driven staff budget depend on them.)*

**Phase 12 — Hardening**
Performance tuning, opening-balance migration wizard, installer + auto-update, backup/restore, documentation, training data set, user manual per vertical, acceptance test pack.

---

## 17. DEFINITION OF DONE (every phase)
- Builds clean, zero warnings in Core/Application.
- Unit + integration tests pass; new accounting paths have balance & reconciliation tests.
- Migrations run from empty database to current, and seed data loads.
- Permissions defined and enforced server-side for every new feature.
- Audit entries written for every create/update/delete/post/approve/print.
- All reports for the phase are seeded, runnable, and exportable to PDF and Excel.
- Licence gating applied to the new module.
- `docs/PROGRESS.md`, `docs/reports/CATALOGUE.md` and any ADRs updated.
- A short demo script in `docs/demos/phaseN.md` showing how to exercise the feature end to end.

---

## 18. FIRST TASK
Start with **Phase 0 and Phase 1**. Before writing code:
1. Confirm the stack choices above (or raise ADRs for any you'd change, with reasons).
2. Produce the full solution skeleton and the initial PostgreSQL schema for `sys`, `sec` and `audit`.
3. Produce the permission catalogue as a seed file (module → feature → available operation flags).
4. Build the permission grid UI and the licensing generator + validator.
5. Show me a running shell that logs in, reads a licence, and displays only licensed + permitted menu items.

Then stop and wait for my review before Phase 2.
