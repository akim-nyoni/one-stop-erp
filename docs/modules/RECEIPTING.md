> **One4All note:** this companion prompt was written against the master brief's original name ("Meridian") and .NET 8. In this repository the product is **One4All** on **.NET 10** (ADR-0001) with **PostgreSQL 18** (ADR-0002); read those substitutions throughout. Everything else — the posting engine, permissions, audit, workflow, licensing and reporting engine — applies as written.

# BUILD PROMPT — Module: CASHIERING & RECEIPTING (`RCP`)

> **Companion prompt to `docs/BRIEF.md` (Meridian ERP).** Everything in the master brief still applies: .NET 8, WPF + MVVM, ASP.NET Core application server, PostgreSQL, the §3.2 posting engine, §5 permissions, §6 audit trail, §7 workflow, §11 licensing and §13 reporting engine. This document specifies **one module** in full. Read the master brief first; do not re-litigate stack decisions here.
>
> Save as `docs/modules/RECEIPTING.md`. Module code `RCP`, database schema `rcp`.

---

## 0. WHY THIS IS NOT THE POS MODULE

**Read this section before writing a line of code. Getting the distinction wrong is the most expensive mistake available on this module.**

A supermarket till and a council cashier's window look superficially alike — a person, a screen, a drawer, a printer — and they are fundamentally different transactions:

| | **POS (§4.1)** | **Receipting (this module)** |
|---|---|---|
| What is being sold | Goods or services from a catalogue | **Nothing. Money is being collected against a debt already billed.** |
| The transaction starts with | An item | **An account** — a property, a meter, a stand, a student, a tenant, a debtor |
| Revenue | Recognised now (Dr Cash, Cr Revenue, Dr COGS, Cr Stock) | **Already recognised when the bill was raised.** The receipt is Dr Cash, **Cr Debtor Control** |
| Stock | Depletes | **Never touched** |
| Price | Comes from a price list or promotion | **Comes from the customer's wallet.** The person pays what they can pay |
| The hard problem | Finding the product fast | **Finding the right account, and allocating the money to the right services in the right order** |
| Output | A sales receipt / tax invoice | **An official receipt, and an updated account balance** |
| Failure mode | Wrong price charged | **Money credited to the wrong resident's account** — which is discovered months later and destroys trust |

**The single most damaging error this module can make is to post a resident's water payment to revenue.** The revenue was recognised when the bill was raised in §4.9. Posting it again on receipt overstates income and leaves the debtor control account permanently wrong. This is the classic council accounting failure and half of this specification exists to prevent it. **A receipt against a debtor account NEVER credits an income account.** Only a *sundry* receipt — money for something never billed — credits income directly, and even then only to a properly coded revenue vote.

Build this as a **separate application window and a separate module**, sharing the session/drawer/banking/device infrastructure with POS but sharing none of its transaction model.

---

## 1. SCOPE — WHO USES THIS AND FOR WHAT

- **Municipality / council**: residents paying water, rates, refuse, sewer, licences; sundry counter income (burial fees, clearance certificates, tender documents, hall hire, market stall fees, building plan fees, parking, pound fees, penalties); consumer deposits; prepaid water/electricity tokens.
- **Housing department (§4.10)**: stand instalments, rent, waiting-list registration fees, transfer and cession fees, deposit payments.
- **Schools (§4.11)**: guardians and sponsors paying fees, uniform deposits, trip payments, sundry income.
- **Any business with a credit ledger (§4.5)**: trade debtors paying invoices at a counter, deposits, sundry cash sales that do not involve stock.
- **Field and agent collection**: rent collectors, market stall fee collectors, third-party agent shops.

Licence-gate it as `RCP`. It must run **with or without** the Municipality, Housing and Schools modules — when none is licensed, it operates against plain AR customer accounts.

---

## 2. CORE CONCEPTS

- **Payer account** — the thing money is received against. A single abstraction over several registers: `AR customer · Municipal consumer account (property/stand) · Housing beneficiary or tenant · Student or guardian or sponsor · Employee (staff debtor) · Supplier (refund received) · Anonymous (sundry receipt with no account)`. Implement as `IPayerAccountProvider` per register, so a new vertical plugs in without touching the receipting screen.
- **Receipt** — an immutable document. Header: receipt number, date, cashier, station, session, payer account (or anonymous), currency, total, status. Lines: what the money is for. Tenders: how it was paid. Allocations: which bills or charges it settled.
- **Revenue vote / income code** — for sundry receipts, a picker of income codes (GL account + cost centre + department + fund/vote), *not* a product catalogue. This is the sundry equivalent of an item, and it must be selectable by tile, by code, or by live search.
- **Session** — a cashier's shift at a station, with opening float, transactions, and a cash-up.
- **Station** — the physical counter, with its own receipt series, printer, drawer and device profile.

---

## 3. CASHIER SESSION LIFECYCLE

`Open session → Transact → (Pickups / drops) → X-read → Close & count → Supervisor verify → Hand over to treasury → Banked`

- **Open**: cashier logs in by PIN or card at a station, declares the opening float **per currency** with a denomination breakdown, and the session locks that station to that cashier. One open session per cashier per station; a cashier cannot hold two open sessions.
- **Exchange rate at session open**: capture the rate of the day per currency and **lock it for the session** (configurable alternative: live rate per transaction). Whichever policy, the rate used is stored on every receipt and tender line and is never inferred later.
- **Pickups / drops to safe** mid-shift, dual-signed, reducing drawer exposure; **petty cash pay-outs** at the counter are a separate permission and post to their own expense coding with a reason.
- **X-read**: mid-shift, non-resetting, prints totals by tender, currency and revenue head — the supervisor's spot-check tool.
- **Close**: cashier counts the drawer **per currency, by denomination**. Support **blind cash-up** (cashier cannot see the expected figure until after counting) as the default for councils. The system computes variance per currency.
- **Supervisor verification**: a second person verifies the count, records the variance, and either accepts it or raises a shortage/overage document that posts to the shortages account and, where policy requires, raises a staff debtor for recovery.
- **Immutability**: once closed and verified, a session cannot be reopened. Corrections are separate, approved documents.

---

## 4. THE RECEIPTING SCREEN

Full-screen, touch-friendly, keyboard-first, designed for a queue of impatient people. Layout:

- **Top**: station, cashier, session, currency rates in force, online/offline state, queue counter.
- **Left — Account search (the heart of the screen).** Live incremental search returning in <150 ms across **every register at once**, matching on: account number, consumer number, **meter number**, **stand/erf number**, property address or street, national ID, surname and first name, phone number, student number, vehicle registration, licence number, agreement number, and old/legacy account number from the migrated system (residents will quote the number on a ten-year-old bill — support it permanently). Results show enough to disambiguate: name, address, account type, balance. Fuzzy matching with `pg_trgm` and `unaccent`, because names will be misspelled and addresses abbreviated.
- **Centre — Account panel.** Once selected: name and address, account type and status, **current balance and ageing (30/60/90/120+)**, last payment date and amount, active payment arrangement and its status, services linked (water, refuse, rates, sewer…) each with its own balance, meter number and last reading, any block/caveat, and any message flagged for the counter ("disconnected — reconnection fee payable", "estate in administration, refer to supervisor"). A **Print mini-statement** button right here — residents ask "what do I owe and for what?" before paying, and the answer must be one tap away.
- **Right — Payment panel.** Amount, **numberpad**, currency selector, tender type tiles, allocation mode, reference/narration, and a large **Receipt** button.
- **Bottom — Sundry tiles.** Quick-pick tiles for common counter income (clearance certificate, burial fee, tender document, photocopy, hall hire deposit, market stall daily fee, licence application), each configured as fixed-amount or open-amount, colour-coded, arranged per station.
- **Mixed receipts are allowed**: one receipt may pay R400 against the water account *and* R50 for a clearance certificate. The lines carry different postings; the receipt is one document with one number.

**Speed requirements**: from account selected to receipt printed, under 5 seconds for a straightforward cash payment. Every field reachable by keyboard. No modal dialog that cannot be dismissed with Escape.

---

## 5. ALLOCATION ENGINE (the second-hardest problem after search)

When a resident pays R500 against an account owing R1,800 across water, rates and refuse, **where does the money go?** This is policy, often statutory, and must be configurable — never hardcoded.

- **Allocation strategies**, selectable as a policy per account type and per service, with a system default and a per-receipt override under permission:
  - **Oldest first (FIFO)** across all charges
  - **By service priority** — a configured ranking (e.g. interest → water → sewer → refuse → rates), exhausting each before the next
  - **Pro-rata** across outstanding services by proportion of balance
  - **Charges before interest**, or **interest before charges** (jurisdictions differ and clients argue about it — make it a setting)
  - **Current period first** (keeps the resident out of further arrears)
  - **User-directed** — the cashier explicitly allocates, with permission and reason
  - **Arrangement-aware** — where a payment arrangement exists, satisfy the arrangement instalment first, then apply the remainder by the default rule
- **Prepaid credit-off-arrears rule**: where a resident buys a prepaid token, a configured percentage of the purchase is diverted to arrears before the token value is issued. Show the split clearly on screen and on the receipt, because the resident will query it.
- **Partial allocation** and **unallocated remainder**: an overpayment becomes a **credit on account** (a liability, not income) and is reported.
- **Payments on behalf of another account**, and **bulk payments** — an employer paying for 200 staff, a landlord paying for 30 properties, a sponsor paying for 12 students — captured as one receipt with a schedule, allocated across accounts, with an import from Excel/CSV and a pre-commit allocation preview.
- The allocation the engine proposes is **always shown before the receipt is committed**, and is printed on the receipt so the resident can see which service their money went to. Opaque allocation is the number one cause of counter disputes.

---

## 6. MULTI-CURRENCY AT THE COUNTER

- Accept any enabled currency; the resident's **account is denominated in one currency** but may be paid in another, converted at the session or live rate, with the rate printed on the receipt.
- **Multi-tender across currencies on one receipt** (part USD cash, part local mobile money), each tender line carrying its own currency, rate and base value.
- **Change** given in a configured change currency, rounded to available denominations, with the rounding difference posted to the rounding account. Where change cannot be given, support **change as credit on account** (common practice, and better than an argument) — clearly stated on the receipt.
- The **FX difference between the rate at billing and the rate at receipt is a realised FX gain or loss** (§3.3) and posts to the FX accounts. It is not a discount and must never be netted against revenue.
- Denomination capture per currency for cash-up.

---

## 7. TENDERS AND PAYMENT CHANNELS

Cash · card (with terminal integration hook and authorisation code capture) · mobile money / wallet (with transaction reference, and a reconciliation against the provider's settlement file) · bank transfer or direct deposit (capture bank, date, reference, and mark pending until the statement confirms) · cheque · voucher · credit on account · journal/contra · agent collection.

- **Cheques**: cheque register (bank, number, date, drawer), and a **dishonoured cheque workflow** — reverse the receipt, restore the debt, raise the dishonour penalty, notify, and report. Never delete the original receipt.
- **Pending/unconfirmed tenders** (deposits claimed but not yet on the bank statement) sit in a distinct state and a distinct GL account until confirmed by the §4.7 bank reconciliation, then clear automatically.
- Tender types are configured records with their own GL mapping, whether they open the drawer, whether they require a reference, whether they permit change, and whether they are subject to a charge.

---

## 8. BULK, FILE-BASED AND UNALLOCATED RECEIPTS

Councils receive most of their money **without the payer ever visiting a counter**. Treat this as a first-class path, not an import utility.

- **Import sources**: bank statement (CSV/OFX/MT940/CAMT.053), mobile money settlement file, card acquirer settlement, employer deduction schedule, agent collection file, third-party payment aggregator.
- **Auto-matching** by account number in the payment reference, with a rules engine (regex/prefix extraction, because payers type the reference badly), fuzzy matching on payer name, and amount-plus-name heuristics. Everything matched is receipted automatically with an import batch reference.
- **The suspense queue** — money received that cannot be matched. This is where councils lose millions and where residents stand at the counter shouting that they paid. Requirements:
  - Unmatched receipts post to a **suspense/unallocated control account**, never to revenue and never to a guessed account.
  - A dedicated workbench: search the suspense pool by amount, date, reference fragment, payer name; search the account registers; allocate with a reason; split one deposit across accounts.
  - **Ageing of the suspense pool** reported weekly, with an escalation for items over 30 days.
  - A resident-facing search: given a bank reference or amount and date, find their money.
  - Allocating from suspense is a permissioned, audited act with a reason — because it is also a fraud route.
- **Reversal of an imported batch** in full, where a file was loaded twice — detected by file hash, and blocked before it happens.

---

## 9. CANCELLATIONS, REVERSALS AND REFUNDS

- **Nothing is ever deleted.** A receipt is cancelled or reversed, never removed.
- **Same-session cancellation**: supervisor PIN, mandatory reason from a configured list plus free text, original receipt marked cancelled, a cancellation document printed, cash returned.
- **Post-session reversal**: a dated reversing document in the current open period, approved through §7 workflow, restoring the debt and reversing the allocation exactly.
- A cashier **cannot cancel their own receipt** above a configurable value threshold, and cannot cancel any receipt after the session is verified.
- **Refunds** — overpayments, deposit refunds, duplicate payments, cancelled services: an approved refund document that pays through §4.6/§4.7 (EFT, mobile money or cash from the safe, never from the till drawer), with the beneficiary bank details verified and the approval trail retained.
- **Deposit refunds** release the deposit liability, less any deductions, with the computation printed.

---

## 10. BANKING AND TREASURY

`Session close → Supervisor verification → Treasury receipt of cash → Consolidation across sessions → Deposit slip → Bank → Statement confirmation → Reconciliation`

- Treasury/safe module: receives cash from multiple cashiers with dual-custody sign-off, consolidates by currency, produces a **bank deposit slip** per bank account and currency, records the deposit reference, and marks the deposit banked.
- **Cash in transit** is its own GL account between the till and the bank, and it must clear. Report anything sitting in it beyond N days.
- Bank confirmation flows through §4.7 bank reconciliation; a deposit that never appears on the statement is an exception report, not a mystery.

---

## 11. AGENTS, FIELD COLLECTION AND OUTLETS

- **Third-party agents** (shops, kiosks, aggregators) collecting on the council's behalf: agent register, their own receipt series or reference range, commission terms, collection files, remittance schedule, reconciliation of collected-versus-remitted, and an **agent ageing report** for money held and not remitted.
- **Field collectors** (rent, market stalls, parking, pound fees) on a handheld or tablet running the offline client, with pre-numbered electronic receipt ranges issued to a named collector, daily reconciliation on return, and per-collector variance reporting.
- **Pre-numbered manual stationery control** — because the power will go out and someone will write a manual receipt. Register receipt books by serial range, issue them to a named cashier against signature, and require the manual receipts to be captured into the system with their original serial number and date. Report unreturned books, unused serials and gaps. This is a real control that real auditors test.

---

## 12. OFFLINE OPERATION

Same architecture as POS: local encrypted store, queued transactions with idempotency keys, sync on reconnect, visible state indicator. Additional receipting-specific rules:

- The cached account balance may be stale. **Print the "balance as at" timestamp on the receipt when offline**, and never let an offline balance be presented as current.
- Allocation is computed on sync against the server's authoritative balance; where the offline allocation differs, the server's allocation wins and the difference is reported, never silently discarded.
- Receipt numbers come from a **pre-issued range allocated to the station**, so numbers stay gapless and cannot collide.
- Offline receipts over a configurable value require supervisor authorisation at capture.

---

## 13. HARDWARE

Thermal receipt printers (58/80 mm ESC/POS), A5/A4 printers for formal receipts, **dot-matrix printers for pre-printed multi-part stationery** (councils still use carbon triplicate and the module must drive it), cash drawers, customer-facing pole display showing the amount and the balance, card terminals, note counters/validators, signature pads, and fiscal devices where required. All behind the §2.3 `IDeviceProvider` interfaces with simulators.

---

## 14. ACCOUNTING TREATMENT (Chartered Accountant's ruling — non-negotiable)

Every receipt posts through the §3.2 posting engine. The permitted patterns are:

| Receipt line type | Posting |
|---|---|
| Payment against a billed debtor account | Dr Cash/Bank/Mobile/Card control · **Cr Debtor Control** (AR / municipal / housing / school sub-ledger) |
| Sundry income never previously billed | Dr Cash · **Cr the specific income vote** · Cr Output VAT where applicable |
| Deposit received (consumer, key, hall, rental) | Dr Cash · **Cr Deposit Liability** — never income |
| Prepaid token purchase | Dr Cash · Cr Prepaid Liability / Cr Debtor Control for the arrears portion |
| Unidentified receipt | Dr Cash · **Cr Suspense/Unallocated Control** — never income, never a guessed account |
| Overpayment | Dr Cash · Cr Debtor Control to nil, remainder **Cr Credit on Account (liability)** |
| Change given / rounding | Dr/Cr Rounding Difference |
| FX on a foreign-currency payment of a billed debt | Dr/Cr Realised FX Gain/Loss |
| Till shortage or overage | Dr/Cr Cash Shortages & Overages, with a staff debtor raised where policy requires |
| Cancellation or reversal | Exact reversal of the original, dated in an open period |

Additional rules:
1. **Receipt date is the transaction date**; the value date may differ for pending tenders. Back-dated receipting is a separate permission, blocked outside an open period, and separately reported.
2. **Cash, cash-in-transit, suspense, deposits and credit-on-account are all control accounts** — locked from manual journals, and each must be reconciled monthly with a shipped reconciliation report.
3. The receipting module's total collection for a period **must reconcile exactly** to: movement on the debtor control accounts attributable to receipts, plus sundry income, plus deposit liability movement, plus suspense movement. Ship that reconciliation as a report and make it part of the month-end checklist.
4. VAT applies to sundry income at the tax point of the receipt; it does **not** apply again to a debtor payment where VAT was accounted for at billing. Get this wrong and the client pays VAT twice.

---

## 15. CONTROLS, PERMISSIONS AND SEGREGATION OF DUTIES

**Hard rules, enforced server-side and at the database where possible:**
- A cashier **cannot receipt against their own account**, their spouse's, or any account flagged as related to them — the receipt is refused and the attempt is logged.
- A cashier cannot cancel their own receipt above the threshold, cannot verify their own cash-up, and cannot bank their own takings unaccompanied.
- No negative receipts. No zero receipts. No receipt without either a payer account or a sundry income code.
- Receipt numbers are **gapless per station**, allocated inside the posting transaction; gaps are impossible by construction and a gap-analysis report proves it.
- Allocation overrides, back-dating, manual-stationery capture, suspense allocation and refunds each require their own permission and each carry a mandatory reason.
- Reprints are permitted but watermarked **COPY**, numbered, and logged.

**Permissions for the §5 grid:**
`Receipting.Session · Receipting.SessionVerify · Receipting.Receipt · Receipting.SundryReceipt · Receipting.Deposit · Receipting.AllocationOverride · Receipting.BackDate · Receipting.Cancel · Receipting.Reverse · Receipting.Refund · Receipting.RefundApprove · Receipting.Reprint · Receipting.Pickup · Receipting.PettyCashPayout · Receipting.BulkImport · Receipting.SuspenseAllocate · Receipting.Banking · Receipting.BankingApprove · Receipting.Agent · Receipting.AgentReconcile · Receipting.Stationery · Receipting.FieldCollection · Receipting.Enquiry · Receipting.Statement · Receipting.Reports`

**SoD pairs to pre-load as violations:** receipt + verify cash-up; receipt + cancel receipt; receipt + bank takings; allocate suspense + approve refund; maintain payer account + receipt against it; issue stationery + capture manual receipts.

---

## 16. REPORT CATALOGUE (`RC` series)

Seed every one of these as a report definition in the §13 engine. Common parameters: Company, Station, Cashier, Session, Date/period range, Currency, Tender type, Revenue vote, Service, Ward/Zone, Account type.

**Daily operations**
- RC1. **Cashier Cash-Up / Session Close Report** (expected vs counted per currency and denomination, variance, supervisor sign-off) — printed and signed at every shift end
- RC2. **X-Read** (mid-shift, non-resetting) and **Z-Read** (session close, sequential, immutable)
- RC3. **Daily Collection Summary** (by station, cashier, tender type, currency, revenue head) — the report the finance office looks at every morning
- RC4. **Receipt Listing / Cashier Day Book** (every receipt, with payer, amount, tender and allocation)
- RC5. **Collection by Revenue Vote / Income Code** (sundry income analysed to the GL vote)
- RC6. **Collection by Service** (water, rates, refuse, sewer, licences, fees — what residents actually paid for)
- RC7. Collection by Tender Type and Channel (counter cash, card, mobile money, bank, agent, field)
- RC8. Collection by Currency, with the rates applied and the realised FX effect
- RC9. Collection by Ward / Zone / Suburb / Class / Scheme
- RC10. Hourly Transaction Volume & Queue Analysis (transactions per hour per station, average handling time, peak periods) — for staffing the counters
- RC11. Cashier Productivity (receipts processed, value, average handling time, variances, cancellations, reprints)

**Reconciliation and control**
- RC12. **Receipting-to-GL Reconciliation** (total collected = debtor control movement + sundry income + deposit movement + suspense movement) — the month-end control report
- RC13. **Cash Control Account Reconciliation** (till → safe → cash in transit → bank)
- RC14. **Till Variance Report** (shortages and overages by cashier and period, with recovery status)
- RC15. **Cancelled & Reversed Receipts** (with reason, authorising supervisor, and value)
- RC16. **Receipt Reprint Log**
- RC17. **Receipt Number Gap Analysis** per station (must show no gaps)
- RC18. **Back-Dated Receipt Report**
- RC19. **Allocation Override Report** (where a cashier directed money against the default policy, with the reason)
- RC20. **Manual / Pre-Numbered Stationery Control** (books issued, serials used, serials unaccounted, books unreturned)
- RC21. **Receipting Audit Trail** (every receipt, cancellation, allocation, suspense movement and refund — before/after values, user, station, terminal, IP)
- RC22. **Segregation of Duties Violations — Receipting**
- RC23. Receipts Against Related-Party Accounts (blocked attempts and any granted exceptions)

**Banking and treasury**
- RC24. **Banking / Deposit Slip** per bank account and currency
- RC25. **Daily Banking Summary** (collected vs handed to treasury vs banked vs confirmed on statement)
- RC26. **Cash in Transit Ageing** (anything unbanked or unconfirmed beyond N days)
- RC27. Unbanked Collections Report
- RC28. Pickups, Drops and Petty Cash Pay-outs Report

**Suspense, imports and pending**
- RC29. **Unallocated / Suspense Receipts Register** with **ageing** — the report that recovers lost money
- RC30. Suspense Allocation Report (what was allocated out, by whom, to which account, with the reason)
- RC31. **Bulk Import Batch Report** (file, records, matched, unmatched, duplicates rejected, total value)
- RC32. Import Matching Exceptions (unmatched references, ambiguous matches, amount mismatches)
- RC33. **Pending Tenders Report** (claimed deposits and transfers not yet confirmed on the bank statement, aged)
- RC34. **Dishonoured Cheque / Reversed Payment Register** (with penalties raised and recovery status)

**Payer-facing and account-level**
- RC35. **Official Receipt** (thermal 58/80 mm, A5, A4 and multi-part pre-printed variants) showing allocation by service, balance after payment, currency and rate
- RC36. **Counter Mini-Statement** (last N transactions and current balance) — printed on request in one tap
- RC37. **Full Account Statement** (links to the §4.5 / §4.9 / §4.10 / §4.11 statements — the same document, printable from the counter)
- RC38. Payment History per Account (all receipts, allocations and reversals for one payer)
- RC39. **Credit Balances / Overpayments Report** (residents in credit, with ageing — a liability that must be reported, not quietly kept)
- RC40. **Deposits Held Register** (by payer, type, date received, refundable amount)
- RC41. Refunds Register (requested, approved, paid, outstanding) with turnaround times
- RC42. Payment Arrangements Captured at Counter (new arrangements, honoured, defaulted)
- RC43. Top Payers / Largest Receipts (for review, and for spotting structuring)
- RC44. Accounts With No Payment in N Months (feeds credit control and disconnection lists)

**Agents and field**
- RC45. **Agent Collection Reconciliation** (collected, remitted, outstanding, commission due, per agent)
- RC46. Agent Remittance Ageing (money held by agents and not remitted)
- RC47. **Field Collector Daily Reconciliation** (receipts issued, value, cash returned, variance, per collector)
- RC48. Field Collection Route Performance

**Management and revenue**
- RC49. **Collection Efficiency** (billed vs collected, by service, ward and month — pairs with the municipal collection reports)
- RC50. **Collection vs Revenue Budget** (against the §4.18 revenue budget, by vote and department)
- RC51. Collection Trend & Seasonality (daily, weekly, monthly, year-on-year)
- RC52. Prepaid Token Sales and Credit-off-Arrears Recovery Report
- RC53. **Receipting Executive Dashboard** (today's collections by channel and service, cash unbanked, suspense pool and its age, variance trend, queue load by station)

**Document layouts** (all designer-editable): official receipt (thermal, A5, A4, multi-part), cancellation slip, mini-statement, deposit certificate, refund voucher, bank deposit slip, cash-up sheet, treasury handover form, stationery issue register, agent remittance advice, field collector daily sheet, dishonoured payment notice.

---

## 17. INTEGRATION POINTS

→ **§4.5 AR** and the municipal/housing/school sub-ledgers (allocation and balances) · → **§4.7 Cash Book** (banking, bank reconciliation, pending tender confirmation) · → **§4.9 Municipality** (consumer accounts, meters, services, prepaid, arrears, arrangements, disconnection status) · → **§4.10 Housing** (instalments, rent, fees, deposits, and the clearance check that a transfer depends on) · → **§4.11 Schools** (student, guardian and sponsor payments) · → **§3.2 Posting engine** (every posting) · → **§3.3 FX** · → **§3.5 Tax and fiscalisation** · → **§4.18 Budget** (collection against revenue budget) · → **§6 Audit** · → **§7 Workflow** (refunds, reversals, write-offs) · → **§13 Reporting** (statements, receipts, bursting).

---

## 18. NON-FUNCTIONAL REQUIREMENTS

- Account search across all registers returns in **<150 ms** on 500,000 accounts; the account panel with balance, ageing and services loads in **<400 ms**.
- Account selected to receipt printed: **<5 seconds** for a cash payment.
- 30 concurrent counters against one server with no lock contention on the debtor control account — use per-account row locking, never a table-level or control-account-level lock.
- Bulk import of a 50,000-line bank statement with auto-matching in **<10 minutes**, resumable, with a preview before commit.
- Offline: unlimited queued receipts, sync of 5,000 queued receipts in **<5 minutes**.
- Every receipt write is idempotent under the station's idempotency key — a retried sync never double-posts.

---

## 19. DELIVERY PLAN FOR THIS MODULE

**RCP-1 — Foundations.** Schema `rcp`, payer account abstraction with the AR provider, receipt/tender/allocation model, number series per station, posting patterns from §14 with tests asserting each pattern balances and hits the right control account.

**RCP-2 — Session and counter.** Session lifecycle, float, pickups, X/Z reads, blind cash-up, supervisor verification, variance posting, device layer with simulators.

**RCP-3 — The screen.** Multi-register live search, account panel with balance and ageing, payment panel, numberpad, sundry tiles, mini-statement print, receipt printing across thermal/A5/A4.

**RCP-4 — Allocation engine.** All strategies, policy configuration, arrangement awareness, prepaid credit-off-arrears, preview-before-commit, override with reason.

**RCP-5 — Multi-currency and tenders.** Session rate lock, multi-currency tendering, change and rounding, realised FX, all tender types, pending tender state, cheque register and dishonour.

**RCP-6 — Bulk, suspense and imports.** File importers, matching rules engine, duplicate-file detection by hash, suspense workbench and ageing, bulk/employer/landlord payments.

**RCP-7 — Cancellations, refunds, banking.** Cancellation and reversal workflows, refund approval and payment, treasury consolidation, deposit slips, cash-in-transit control.

**RCP-8 — Agents, field collection, offline.** Agent register and reconciliation, field collector ranges and daily reconciliation, pre-numbered stationery control, full offline client with sync.

**RCP-9 — Reports and hardening.** All `RC`-series reports seeded and exportable, the §14 reconciliation proven on a month of generated data, performance tuned to §18, licence gating on `RCP`, permissions and SoD loaded.

---

## 20. DEFINITION OF DONE

- Every posting pattern in §14 has a test asserting Dr = Cr, the correct control account, and the correct sub-ledger effect.
- **RC12 reconciles to zero** on a generated month containing every transaction type including reversals, dishonours, FX payments, overpayments and suspense allocations.
- A cashier cannot: receipt to their own account, cancel their own receipt above threshold, verify their own cash-up, or create a receipt number gap. Each has a failing-path test.
- Offline: 1,000 receipts captured with the network down, synced, and reconciled with zero duplicates and zero lost transactions.
- All `RC` reports seeded, runnable, exportable to PDF and Excel, and printable to thermal where applicable.
- Permissions, SoD pairs and licence gating in place; audit rows written for every receipt, cancellation, allocation, suspense movement, refund and reprint.
- `docs/PROGRESS.md` and `docs/reports/CATALOGUE.md` updated.

---

## 21. FIRST TASK

Start at **RCP-1 and RCP-2**. Before writing code:
1. Restate the posting patterns in §14 as a table of concrete GL entries for a worked example: a resident owing 1,800 across three services pays 500 in a foreign currency, with 50 of it for a clearance certificate. Show every line, in transaction and base currency.
2. Propose the `rcp` schema (tables, keys, indexes, constraints, the unique partial index guaranteeing one open session per cashier per station, and the gapless number allocation approach).
3. Define `IPayerAccountProvider` and implement it for AR only.
4. Build the session lifecycle with cash-up and supervisor verification.
5. Show me a session opening with a float in two currencies, a single receipt posting correctly to the debtor control account, and a blind cash-up producing a variance that posts to shortages.

Then stop for review before RCP-3.

---

## IMPLEMENTATION STATUS (One4All)

- **RCP-1 + RCP-2 core: DONE** — `rcp` schema; `CashierStation`, `CashierSession` (opening float, blind cash-up variance), `Receipt`/`ReceiptLine`/`ReceiptTender`; `IPayerAccountProvider` (AR); `IReceiptingService` (open session → receipt → close with variance). Debtor payments post **Dr cash / Cr debtor control** via the AR ledger and allocate oldest-first (never credit income); sundry credits the income vote; deposits credit the deposit liability; unidentified money credits the suspense control; overpayment remainder credits credit-on-account; blind cash-up posts the shortage/overage. Zero/negative receipts and receipts with neither account nor sundry code are refused. Integration test asserts each pattern balances, hits the right control account, and reduces the resident's debt. See `docs/PROGRESS.md`.
- **Remaining (RCP-3…RCP-9):** the WPF receipting screen and multi-register live search, the full allocation-strategy set with preview/override, multi-currency tendering + realised FX at the counter, bulk/suspense import workbench, cancellations/refunds/banking/treasury, agents & field collection & stationery control, offline client, and the full `RC`-series report catalogue.
