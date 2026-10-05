# Report Catalogue Backlog (brief §14)

Gap analysis of brief §14 against `docs/reports/CATALOGUE.md` (2026-10-05). Housing (I-B, the H series) is complete and
omitted. **Data exists** = the entities / services to build the report are already in the system; anything else names the
missing domain data. Items are struck through as they ship (with the report code).

## A. General Ledger / Financial
- 2 Trial Balance multi-currency — data exists
- 5 Journal Listing & Audit (unposted / reversing / recurring) — data exists
- 8 Statement of Changes in Equity — data exists
- 10 Notes-to-accounts schedules — needs account → note mapping
- 11 Management Accounts Pack — data exists (forecast part needs forecasts)
- 12 Budget vs Actual vs Commitment — data exists
- 14 Consolidation elimination schedule — data exists
- 15 FX Revaluation report; 16 Realised FX gain/loss — data exists
- 17 Suspense & unallocated review; 18 VAT / bank control legs — data exists
- 20 Year-End Pack; 22 Recurring journals due / missed; 23 Accruals & prepayments — data exists
- 24 Intercompany balances & reconciliation; 25 Hyperinflation working papers — data exists

## A-B. Budgeting (B codes)
- Data exists: B2, B12, B15–B18, B20, B24–B26, B28, B33, B34, B36, B37, B44, B45; partly B14, B19, B21, B27, B42
- Needs new data: B1, B3–B10 (workflow, targets, hierarchy, drivers, establishment, capital projects), B13 (stage snapshots), B22, B23, B29, B30, B32 (budget holders), B35, B38–B41 (forecasts, cash budget)

## B. Accounts Receivable
- ~~29 Aged detail (AR29)~~, ~~30 by rep / currency (AR30)~~, ~~31 Customer Ledger (AR31)~~, ~~33 Sales Day Book (AR33)~~, ~~35 Top Customers (AR35)~~, ~~39 / 44 Unallocated & advances (AR39)~~, ~~40 DSO (AR40)~~, ~~41 Promise-to-Pay (AR41)~~, ~~43 Write-offs (AR43)~~, ~~45 Lay-byes (AR45)~~, ~~47 Exposure by Currency (AR47)~~, ~~48 New & Dormant (AR48)~~
- Data exists: 34 / 36 sales analysis and history (needs item-level sales from SO / POS lines), 38 collections (all receipts), 49 price list, 50 layouts (quotation, SO confirmation, delivery note, remittance slip)
- Needs new data: 28 (parent / child customers), 37 reason codes

## C. Accounts Payable
- ~~53 Supplier Ledger (AP53)~~, ~~54 Purchase Day Book (AP54)~~, ~~57 / 58 Payments Made incl. runs (AP58)~~, ~~59 GRNI (AP59)~~, ~~60 Matching Exceptions (AP60)~~, ~~62 WHT Deducted (AP62)~~, ~~63 Scorecard (AP63)~~, ~~64 Balances with debit flags (AP64)~~, ~~65 Bank Detail Change Audit (AP65)~~, ~~66 Top Suppliers (AP66)~~
- Data exists: 55 purchase analysis by item, 67 layouts (requisition, GRN, debit note)
- Needs new data: 61 (supplier's own statement capture), cheque entity

## D. Cash Book / Treasury
- Data exists: 68, 70, 72 (13-week, by currency), 73, 74
- Needs new data: 75 cheque register, 76 wallet statements

## E. Inventory
- Data exists: 78, 79, 81, 86, 87, 92, 93, 94 (labels), 96, 97, 100 (partly)
- Needs new data: 82 reason codes, 88 batches, 89 serials, 90 expiry, 91 in-transit, 98 BOM, 99 yield, 101 consignment

## F. POS / Retail
- Data exists: 103, 104, 105, 112–114, 117, 120
- Needs new data: 106 void reasons, 107 refunds, 108 line discounts, 109 overrides, 110 no-sale, 111 parked sales, 115 loyalty, 116 POS reprint log, 121 fiscal submission log

## G. Procurement
- ~~124 Requisition turnaround (PO124)~~, ~~125 Approval history (PO125)~~, ~~126 / 127 PO listing with overdue (PO126)~~, ~~128 Committed cost (PO128)~~, ~~129 GRN & variance (PO129)~~, ~~130 Rejected goods (PO130)~~, ~~131 RFQ comparison (PO131)~~, ~~132 Price variance (PO132)~~, ~~134 Spend analysis (PO134)~~, ~~135 Lead times (PO135)~~
- Data exists: 136 budget availability at requisition (needs an expense account on requisition lines); partly 133 (no framework contracts)

## H. Sales Order Processing
- Data exists: 137, 139 packing / despatch notes, 143
- Needs new data: 140 OTIF dates, 141 POD, 142 vehicles / routes

## I. Municipality (the M series)
- ~~146 Billing Run Summary (M146)~~, ~~147 Billing Exceptions (M147)~~, ~~148 Aged Debtors by Ward / Category / Service (M148)~~, ~~151 Valuation Roll (M151)~~, ~~152 Property Register (M152)~~, ~~153 Clearance Register (M153)~~, ~~154 Reading Sheets (M154)~~, ~~155 Reading Exceptions (M155)~~, ~~156 Consumption (M156)~~, ~~159 Meter Register (M159)~~, ~~160 Disconnection / Reconnection (M160)~~, ~~161 Arrangements (M161)~~, ~~162 Indigent & Subsidy (M162)~~, ~~166 Licences (M166)~~, ~~167 Ticket SLA (M167)~~, ~~168 Tariff Schedule (M168)~~, ~~149 Arrears (M149)~~, ~~163 Interest (M163)~~, ~~164 Write-offs (M164)~~, ~~169 Revenue by Source (M169)~~, ~~170 Ward Pack (M170)~~, ~~171 Statutory Age Analysis (M171)~~, ~~172 Prepaid vs Conventional (M172)~~
- Data exists: 145 bill layout (a document template), 158 (partly)
- Needs new data: 157 water balance (bulk supply readings)

## J. Schools
- Data exists: 174–178, 181, 182; partly 180, 184
- Needs new data: 179 sponsors, 183 transport, 185 student wallet, streams

## K. Construction / Job Costing
- Data exists: 186, 188 IPC layout, 197; partly 189, 190, 195
- Needs new data: 191 variations, 192 subcontractors, 193 timesheets, 194 plant, 196 BOQ

## L. Weighbridge
- Data exists (thin): 198, 199
- Needs new data: customer / driver / time in-out / overrides / stockpiles / mixes / haulage (200–205)

## M. Fixed Assets
- Data exists: 207, 208, 209, 212
- Needs new data: 210 counts, 211 CWIP, 213 insurance / warranty, 214 planned maintenance

## N. Tax & Statutory
- Data exists: 216–218, 220, 224, 225; partly 219, 221, 222
- Needs new data: tax tables

## O. Payroll & HR (PR codes)
- Data exists: PR2, PR3, PR5, PR6, PR12, PR16, PR17, PR29, PR38, PR49, PR59–PR61, PR64, PR65, PR67, PR70; partly PR4, PR24, PR26–PR28, PR34, PR39, PR45, PR46, PR58, PR63, PR66
- Needs new data: payroll runs and approvals, employee bank / tax / ID details, schemes and members, tax tables, timesheets, grades and establishment, HR records (PR7–PR11, PR13–PR15, PR19–PR23, PR25, PR30–PR33, PR35, PR36, PR40–PR43, PR47, PR48, PR50–PR57, PR62, PR68, PR69)

## P. Audit, Security & Compliance
- ~~237 Permission Matrix (SYS237)~~, ~~238 Membership (SYS238)~~, ~~239 SoD all modules (SYS239)~~, ~~240 Master Data Changes (SYS240)~~, ~~242 Back-dated (SYS242)~~, ~~243 Deleted & Voided (SYS243)~~, ~~244 Numbering Gaps (SYS244, journals)~~, ~~245 Unposted (SYS245)~~, ~~248 Licences (SYS248)~~, ~~250 Report Run Log (SYS250)~~
- Data exists: partly 236 (no login history), 241 (overrides), 247 (hash-chain verification is in the audit service)
- Needs new data: 249 login sessions

## Q. Executive / BI Dashboards
- Data exists: 251–255; 256 except water losses

## R. Master Data & System Listings
- ~~259 Exchange Rates (SYS259)~~, ~~260 Number Sequences (SYS260)~~, ~~261 Company Configuration (SYS261)~~, ~~262 Report Inventory (SYS262)~~, ~~263 Jobs & Deliveries (SYS263)~~
- Data exists: 258 price list listing
